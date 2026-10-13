# Deploying Phase 0 to staging

Phase 0 step 0.10; decisions D8, D11 and D12; design section 11. This is for whoever deploys: the developer, not Claude Code.

One build is deployed twice:
- **App site:** `app.` host, with `Site:SiteMode = App`.
- **Admin site:** `admin.` host, with `Site:SiteMode = Admin`.

On a test site without subdomains, a single site with `Site:SiteMode = All` also works (D9).

## 1. Get the build

Every CI run on `mt/**` uploads the artifact `inprem-build-<commit>`. It contains:
- `out/app`: the published app;
- `scripts/deploy`: `prepare-site.ps1` and `app_offline.htm`;
- `scripts/migrations`: the bundles, the per-migration scripts and the verify scripts;
- `scripts/rehearsal`: the rehearsal driver.

To build locally instead:

```powershell
dotnet publish InpremClockingApp/InpremClockingApp.csproj -c Release -o out/app
```

## 2. Make one package per site

```powershell
pwsh scripts/deploy/prepare-site.ps1 -Artifact out/app -SiteMode App   -OutDir out/sites -AppHost app.<domain> -AdminHost admin.<domain>
pwsh scripts/deploy/prepare-site.ps1 -Artifact out/app -SiteMode Admin -OutDir out/sites -AppHost app.<domain> -AdminHost admin.<domain>
```

The script:
- **Sets in `web.config`** (environment variables): `ASPNETCORE_ENVIRONMENT`, `Site__SiteMode`, `Jobs__RunServer` (true for App only), and the two host names.
- **Strips** `appsettings.Development.json` and `appsettings.Production.json`.
- **Refuses** a package containing anything that looks like a credential.
- **Prints** the SHA-256 of each zip. Record it.

## 3. Host configuration (secrets, never in the package)

Set these in each site's control panel, as environment variables or app settings. A server-only `appsettings.Production.json` that you keep on the server also works.

| Setting | App site | Admin site |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | yes (same database) |
| `Bootstrap__CodeHash` | – | until the first owner is set up (runbook section 2) |
| `Security__BreakGlassCodeHash`, `Security__RecoveryApprovers`, `Security__Mailbox` | – | yes |
| `Links__AdminBaseUrl` | – | yes, e.g. `https://admin.<domain>` |

**If you upload with Web Deploy,** don't tick "remove additional files at destination". Otherwise a server-only `appsettings.Production.json` is deleted.

**SmarterASP (D11):** stop the App site from idling, so background jobs run. Use the always-on or keep-alive setting if the plan has one, or an uptime ping every 5 minutes.

## 4. Change window (design 11.4)

1. **Rehearsal:** `docs/multitenancy/rehearsal-phase0.md` is done for this database, and the CTO has signed it.
2. **Close the site:** upload `scripts/deploy/app_offline.htm` to **both** sites. Writes stop.
3. **Final backup:** take one from the control panel and download it. Record the file name, size and SHA-256.
4. **Database:**
   ```powershell
   sqlcmd -S <server> -d <database> -U <user> -I -b -i scripts/migrations/phase0-up.sql
   ```
   It must end with `Phase 0 schema complete.` If it stops with an error, nothing more runs. Fix the cause or roll back (section 6).
5. **Verify:** run the six verify scripts (`scripts/migrations/README.md`, "Running") and compare them with the expected values.
6. **Deploy:** upload `out/sites/App` to the App site and `out/sites/Admin` to the Admin site.
7. **Reopen:** remove `app_offline.htm` from both sites.

## 5. Checks after deployment

- **App site:**
  - sign in as a test admin and open the back office; hours reports match the baseline totals;
  - `/Provider/setup` and `/jobs` return 404.
- **Admin site:**
  - `/` redirects to the provider login;
  - `/setup` exists only while bootstrap is configured and no owner exists;
  - `/health` answers (Degraded until two owners are enrolled);
  - no App page answers (for example `/BackOffice` returns 404).
- **Jobs:**
  - `scripts/migrations/hangfire-verify.sql` shows 1 or more job servers, from the App site only;
  - the Admin site's log says "Hangfire job server not started".
- **Exit check P0-11:** the host-isolation suite runs in-process in CI and must have passed for the deployed commit. On staging, the checks above are the evidence. Record both in `PROGRESS.md`.

## 6. Rollback (design 11.6)

| Level | When | Action |
|---|---|---|
| R1 Application only | The new code fails; the schema is fine | Redeploy the previous build to both sites. The Phase 0 schema is additive, but the previous build has none of the new pages. |
| R2 Down script | The schema change itself causes problems | `app_offline.htm` up; `sqlcmd ... -i scripts/migrations/phase0-down.sql` (must end with `Phase 0 rolled back`); redeploy the previous build |
| R3 Restore | Data damaged, or R2 fails | Restore the final backup from step 4.3, then redeploy the previous build. Writes after the backup are lost. |

**R2 deletes** memberships, provider users, the audit log and jobs. Export the audit log first if it matters.
