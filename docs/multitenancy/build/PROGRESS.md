# Progress log

Claude Code updates this file at the end of every step. Newest entries at the top of the log.

## Current position

- **Phase:** 0 Foundation (in progress)
- **Next step:** Phase 0 exit — the developer's manual checks (P0-6, P0-11, P0-12, P0-5 credential), then the Phase 0 exit review
- **Branch:** `mt/phase0` (from `mt/integration` `1b59b6b`)

## Phase status

| Phase | Status | Exit checks passed | Merged into `mt/integration` |
|---|---|---|---|
| G0 Preparation | Done | G0-2 (on test data; see follow-ups), G0-4, CI, OpenAPI valid, no existing app file changed | 2026-10-13 (`ee940f8`) |
| 0 Foundation | Steps done (0.1–0.8, 0.10; 0.9 moved to Phase 7); exit checks pending | — | — |
| 1 Provider Console | Not started | — | — |
| 2 PayPal core | Not started | — | — |
| 3 Website API | Not started | — | — |
| 4 Invoices | Not started | — | — |
| 5 Twilio | Not started | — | — |
| 6 Staff alerts | Not started | — | — |
| Cutover | Not started | — | — |
| 7 Hardening | Not started | — | — |

## Open follow-ups and shortcuts

- **G0 exit check wording:** `git diff 1061ea8 -- InpremClockingApp/` is not empty; it contains one new file, `InpremClockingApp/Properties/AssemblyInfo.Tests.cs` (`InternalsVisibleTo` for the test projects, approved by the developer 2026-10-12). Read the check as "no existing application file changed".
- **Model drift check:** works with `dotnet-ef` 8 and reports no drift on `1061ea8` (2026-10-13). `InpremClockingApp/.config/dotnet-tools.json` pins `dotnet-ef` **7.0.2**, which has no `has-pending-model-changes`; update the pin to 8.0.31 in Phase 0 (one-line change to an existing file, needs agreement). Until then, see `scripts/inspection/README.md`. `CLAUDE.md`'s note that the model has moved on from the migrations is out of date.
- **Test database modes:** `localdb` (Windows default) and `container` (`INPREM_TEST_DB=container`, Docker Desktop with Linux containers) both pass the Step 1 suite (container mode verified 2026-10-12; image `mcr.microsoft.com/mssql/server:2022-latest`; Testcontainers removes the container after the run). `external` mode is for CI's service container (Step 3).
- **Test-host differences from a normal run** (all in `InpremAppFactory`): environment `Testing` (no user-secrets or per-environment appsettings), `SeedAdmin`/`SeedSuperAdmin` blanked, in-memory data-protection keys, `BillingBackgroundService` not started.
- **House organization in every new database:** migration `MultiTenancy_Phase1a_BackfillTenantData` inserts "Inprem Holistic Community Resource Center" as tenant 1. The seed catalog exposes it as `House`; Alpha, Beta and Gamma are tenants 2–4.
- **Baseline findings F-01 to F-09** (`docs/multitenancy/baseline-findings.md`): recorded, not fixed. Each one has a passing test tagged `Finding`.
  - **F-08 (cross-tenant delete through the Move page handlers) is a security issue.** Phase 0 step 0.5's save interceptor should block it, because the posted entity's TenantId is never the caller's. When that lands, the F-08 test must flip to "Beta unchanged".
  - **The developer decides when to fix F-01 to F-07 and F-09.** Each fix is a deliberate behaviour change and updates its test with a reason.
- **Known secret in the approved app.** `InpremClockingApp/Services/EmailService.cs` has a hardcoded SMTP credential (since commit `10e1e22`). It is listed in `.gitleaksignore` so CI can block new secrets. Before P0-5 it must be rotated, moved to configuration and the ignore line removed. Ownership must be confirmed with the original author before rotating. Gitleaks' default rules miss it; the custom rule `csharp-network-credential` in `.gitleaks.toml` finds it.
- **Vulnerable packages in the approved app.** 9 high/critical transitive advisories, from the 8.0.0 EF Core / Identity packages and from `Microsoft.VisualStudio.Web.CodeGeneration.Design`. They are listed in `scripts/ci/known-vulnerable-packages.txt`, so the CI gate only fails on new ones. Upgrade them in Phase 0 (the app's csproj is not on the C2 list, so the developer needs to agree the change). P0-7 requires 0.
- **CI is green on GitHub.** Run 38326059916 on `ee29210` (pushed 2026-10-13): build and test, secret scan and vulnerable packages all passed. The repository is public (the GitHub API answers without a login); D33 recommends making it private before billing code is merged.
- **G0-2 was run on the test deployment, not production** (developer, 2026-10-13). That backup was the new app's test data, since corrected (odd sessions and time storage included). The new app will not go live on test data. **If the new app is to take over the old (reference) app's real data, inspect that backup first** with the same scripts (use `04p`/`05p` if it has no `TenantId`). If the new app starts fresh, nothing more is needed.
- **Owner backfill (decided in step 0.3).** Migration M2 makes the only admin of an organization its Owner. Organizations with several admins get Admins only, because "the earliest admin" cannot be derived (no creation date on `AspNetUsers`). `platform-M2-verify.sql` lists them, and their Owner is named later (Provider Console, Phase 1, or by hand).
- **"Tenant not Closed" (step 0.3).** There is no Closed state yet; Phase 2 adds subscription and tenant statuses. `Tenant.IsActive` ("Suspended" on the old Platform pages) does not block sign-in today and was left that way. Revisit in Phase 2.
- **Legacy accounts get a membership at first sign-in (step 0.3).** This applies to an account with `AppUser.TenantId` and no membership row at all, e.g. one made by the Admins page, Register, the old Platform pages or `SeedAdmin`; it gets an Active Admin membership. Revoked memberships are never recreated. Step 0.4 makes `CreateAdmin` create memberships itself; remove the fallback once every creation path does.
- **Accounts without any organization are signed out (step 0.3, behaviour change).** This covers accounts that are not SuperAdmin and have no Active membership. Today's data has none; before, such an account could open the kiosk pages.
- **Membership cache (step 0.3).** Validation is cached for 5 minutes. Every code path that changes a membership (revoke, role change, acceptance) must call `IMembershipService.Invalidate`, or the change is seen only when the entry expires (tested).
- **Invitation emails use the approved app's `EmailService` (step 0.4).** They go through `IEmailSender` → `ApprovedAppEmailSender` → `EmailService`, the same mailbox as password resets. That mailbox's credential is hardcoded (see the follow-up above), and moving the settings to configuration fixes both. Tests always replace the sender with a capturing one. **Incident:** during development (2026-10-13), before the capture was in place, one baseline test run triggered an invitation through the real `EmailService` to an invented `@shared.test` address. That address cannot receive mail, but the app very likely connected to the live SMTP server with the hardcoded credential. Ask the mailbox owner to check for it; another reason to rotate.
- **Accounts working in another organization than `AppUser.TenantId`: fixed in step 0.5.** The AppUser filter is membership-based, so a member sees their own account wherever they are a member (tested with the Manage page).
- **Isolation suite vs "403/404" (step 0.5).** The route-by-route suite asserts that nothing of the other organization is revealed or changed, for all 94 routes. Approved (C1) handlers answer a foreign id with their usual not-found response (redirect, 200 with an empty result or message, or 500 when the interceptor refuses), not always 403/404. Making them 404 would mean changing C1 pages.
- **Refused cross-organization saves return 500 (step 0.5).** `TenantIsolationException` ends the request; F-08 attempts now fail this way. A friendlier 403/404 needs a global exception filter (`Program.cs`) and can come later; nothing of the other organization leaks either way.
- **Platform-operator pages (C4) and the fail-closed filter.** With no organization, `UserManager.Users` lists nobody. `TenantAdminService.GetAdminsForTenantAsync` (C4, replaced in Phase 1) now reads users explicitly unfiltered. The C4 pages are otherwise unchanged.
- **BackOffice "Admins" count: fixed** (2026-10-13, developer-approved change to the frozen `Pages/BackOffice.cshtml.cs`). It now counts the same accounts the Admins page lists (`AuthService.GetAll`), so members who joined by invitation are included.
- **Invited memberships do not expire.** An Invitation entity with expiry and hashed tokens arrives in step 0.7; tie member invitations to it then, if wanted.
- **Anti-forgery on new pages.** The approved app ignores anti-forgery tokens globally (until C3-10), but the new "Choose organization" page enforces them (tested).
- **Report baseline R1–R3 are proposed, not agreed.** `05-report-baseline.sql` uses staff hours per organization per month, volunteer hours per organization per month, and dashboard figures. Change them there if the project owner wants others.
- **Website API draft: open questions** (listed in the YAML's `info.description`):
  - Section 7.3 has 17 endpoints; the G0 prompt says 18. The 7.6 diagram's `POST /account/checkout-sessions` conflicts with the table. The draft follows the table.
  - The CAPTCHA field and consent flags are not named in the design (the draft uses `captchaToken`, `acceptTerms`, `acceptPrivacyPolicy`, `smsOptIn`).
  - Plan limits, invoice statuses and the staging hostname are placeholders.
- **Test project naming.** The Phase 0 file says to create `Inprem.Platform.Tests`; `TESTING.md` lists `.Unit`, `.Integration` and `.Architecture`, which G0 created. The three existing projects are used.
- **EF Core version.** The platform projects use EF Core **8.0.0**, the approved app's version, so referencing them changes nothing in the app's runtime. Upgrading EF Core (which fixes the known advisories) is still a deliberate step to take with a baseline run.
- **Deploying step 0.2.** Apply `scripts/migrations/platform-M1a-up.idempotent.sql` **before** deploying, because the app keeps its cookie keys in `platform.DataProtectionKeys`. Users are signed out once: the cookie is renamed to `.Inprem.App` and keys move from `App_Data` to the database. `Site:AppHost`/`AdminHost` are empty (any host) until the cutover. See `scripts/migrations/README.md`.
- **No account is created at startup any more (step 0.6).** Existing databases keep their admins. A new, empty database has no account until the provider bootstrap of step 0.7. `ROLES.md` and `Test.md` were updated. P0-5 ("secret scan 0 findings") still needs the `EmailService` credential rotated and its `.gitleaksignore` line removed.
- **Provider console sessions: no 8-hour / 30-minute limits yet (step 0.7).** Design 5.5 asks for 8 hours maximum and 30 minutes idle on the Admin site, plus an IP allow-list. The step 0.7 text doesn't ask for them; they belong with the console in Phase 1. Lockout (5 tries / 15 minutes) already applies.
- **No QR code on the two-factor page (step 0.7).** The app ships no QR library, and none is loaded from outside. The page shows the key and an `otpauth://` link, which every authenticator app accepts by typing. A QR code can come with the Phase 1 console.
- **One Provider Owner until Phase 1 (D16).** Inviting provider users is a Phase 1 console screen. Until then the console banner and `/health` (Degraded) warn, as designed. Level 2 (peer reset) is a tested use case without a screen until Phase 1.
- **Level 4 completes by command after its delay (step 0.7).** `provider recover --complete --id <id>` is run by the operator once the 24 hours have passed. The background jobs of step 0.8 could do it automatically; that is an open choice.
- **Provider emails go through the approved app's mailbox (step 0.7).** Bootstrap and recovery emails use `IEmailSender` → `ApprovedAppEmailSender` → `EmailService`, the hardcoded SMTP account still waiting on the owner's confirmation. "SMS where configured" (design 5.4) comes with Twilio in Phase 5.
- **Bootstrap after an unused, expired first invitation (step 0.7).** A new bootstrap code invites the same person again; the used code is refused for good (runbook section 2).
- **The old billing sweep no longer runs (step 0.8).** Removing `BillingBackgroundService` (C2-2, as the step asks) means no automatic renewal, past-due or suspension of organizations until Phase 2. `BillingService` and the Platform pages (C4) are unchanged.
- **Jobs are off by default (step 0.8).** `Jobs:RunServer` is `false` in `appsettings.json`; the App site's deployment sets it to `true`. There are no jobs yet.
- **Hangfire install script follows the package (step 0.8).** `scripts/migrations/hangfire-up.idempotent.sql` comes from Hangfire.SqlServer 1.8.14's installer. Upgrading the package means regenerating and reviewing it.
- **Rehearsed on synthetic data only (step 0.10).** The Phase 0 bundle was rehearsed with rollback (R2 and R3) on a disposable database with synthetic data, at about the size of the real one. Before a shared or production database, it must be repeated on a restored copy of that database (`rehearse-phase0.ps1 -Existing`). The CTO then signs it (design 11.2–11.4); see `docs/multitenancy/rehearsal-phase0.md`.
- **M1 is M1a + M1b (step 0.10).** The step's "empty platform tables" already exist from step 0.7: provider users, invitations (named `ProviderInvitations`), audit and recovery records. No duplicate migration was added.
- **`appsettings.Production.json` on the server (step 0.10).** `prepare-site.ps1` never ships it (a local publish would include the developer's copy). Upload with Web Deploy *without* "remove additional files", or move the connection string into the host's settings.
- **Tests tagged `ChangedBy`** record behaviour the design changes on purpose: same admin email in two organizations (D2/C3-6) and no lockout (C3-7). Both change in Phase 0 step 0.4.
- **Playwright browsers** are a one-time install per machine: `pwsh tests/InpremClockingApp.Tests.Ui/bin/Debug/net8.0/playwright.ps1 install chromium` (CI must do the same).

## Manual / sandbox test records

_(date — what was tested — result)_

## Log

_(date — phase/step — summary — tests — next)_

- **2026-10-13 — Phase 0 / Step 0.10 — Migrations M1 and deployment rehearsal.**
  - **M1:** already in place as M1a and M1b; reconciled in `docs/multitenancy/rehearsal-phase0.md`. No new migration.
  - **Bundles:** `scripts/migrations/phase0-up.sql` and `phase0-down.sql`, generated by `build-phase0-bundle.ps1` from the six reviewed scripts. Guards: SQLCMD mode, stop on error, right database and no unknown migrations, expected end state. CI rebuilds them and fails on any difference.
  - **Rehearsal:** `scripts/rehearsal/rehearse-phase0.ps1` covers design 11.1–11.6 on a disposable LocalDB database with synthetic data (`seed-rehearsal-data.sql`: 3 organizations, 300 people, 120,000 sessions). It runs:
    1. inspect and fingerprint;
    2. checksummed backup;
    3. up, verify, re-run;
    4. smoke test of the Release build on the migrated database (login, setup 404, back office needs sign-in, health, a job server);
    5. R2 down and re-run;
    6. re-apply, then R3 restore;
    7. the wrong-database guard.
  - **Rehearsal results:** the approved app's data (row counts, hour totals and checksums per organization) stayed identical at every step. Timings: up 1.6 s, down 1.0 s, restore 2.5 s. Recorded in `docs/multitenancy/rehearsal-phase0.md`; `-Existing` repeats it on a restored copy.
  - **Found and fixed during the rehearsal:**
    - the bundle guard failed to compile on a database without the platform schema (checks moved to dynamic SQL / own batch);
    - the synthetic seed lacked the roles real databases have.
  - **Deployment:** `scripts/deploy/prepare-site.ps1` turns the one artifact into an App or Admin package:
    - `web.config` environment variables: environment, `SiteMode`, `Jobs:RunServer` (App only), hosts;
    - per-environment settings files stripped; credential-shaped values refused (proven);
    - SHA-256 printed.
    - `app_offline.htm` is added; `docs/multitenancy/deployment-phase0.md` gives the staging procedure, host settings and rollback.
  - **CI:** new `package` job (no deployment): bundle-freshness check, one `dotnet publish`, both site packages prepared as a self-check, artifact `inprem-build-<commit>` (app plus deploy, migration and rehearsal scripts).
  - **Results:** suites unchanged since step 0.8 (no app code changed in this step).

- **2026-10-13 — Phase 0 / Step 0.8 — Background jobs (D6, C2-2).**
  - Hangfire 1.8.14 with SQL Server storage in schema `hangfire` (`Hosting/JobsHosting.cs`). The packages add no new advisories and no new SQL client (they use EF Core's `Microsoft.Data.SqlClient`).
  - **Storage:** registered on every site. The app never creates the tables (`PrepareSchemaIfNecessary = false`); they come from the reviewed `hangfire-up/down/verify` scripts, rehearsed on LocalDB.
  - **Job server:** starts only when `Jobs:RunServer = true` and the site serves the App, decided at startup. `Jobs:RunServer = true` on an Admin-only site stops startup with a clear error.
  - **Dashboard:** read-only, at `/jobs` (Admin site) or `/Provider/jobs` (`SiteMode=All`), on the Admin host only. It is for active Provider Owners signed in with two-factor, using the same checks as the console (`ProviderAccessCheck`, extracted from the step 0.7 filter).
  - **`BillingBackgroundService` no longer registered** (`Program.cs`); the test host's removal of it is gone too.
  - **Fixed along the way:** `TenantContextMiddleware` signed provider users out on console endpoints outside the Provider area (the dashboard). Such endpoints now carry `ProviderEndpointMetadata` and are left alone. The host-isolation test now requires every Admin-site endpoint to be a Provider page or carry that marker.
  - **Tests (Integration +6):**
    - an App site with `RunServer` starts a server that runs an enqueued job;
    - an Admin site starts none, and refuses `RunServer`;
    - the default is no server, and the billing worker is gone;
    - the dashboard: 401 anonymous, refused for a Support user, 200 read-only for an Owner (a delete is refused and the job stays), the console session kept;
    - not served on the App site.
  - **Results:** Unit 79, Architecture 11, Baseline 320, Integration 66, UI 5. No new advisories.

- **2026-10-13 — Phase 0 / Step 0.7 — Provider users, bootstrap and recovery (design section 5).**
  - **Domain:** `ProviderUser` (Owner / Billing / Support; Invited / Active / Disabled), `Invitation` (token hash, expiry, consumed / revoked), `RecoveryRecord` (Levels 1–4, approvals, Level 4 delay) and `AuditEntry` (append-only).
  - **Application:** `ProviderSecurityService`, one use case per action, shared by the server commands and the setup page:
    - bootstrap and invitations;
    - Level 1 record, Level 2 peer reset (no screen until Phase 1), Levels 3 and 4 with approvals.
    - Codes and link tokens are compared and stored only as SHA-256 hashes (`SecretTokens`).
    - Rate limits: bootstrap 5 failures an hour; break-glass locks for 24 hours after 3 failures; one recovery per account per 24 hours.
    - Every action is audited and emailed to all active owners and `Security:Mailbox`.
  - **Platform migration M1b** (`ProviderUsers`, `ProviderInvitations`, `RecoveryRecords`, `AuditEntries`, `UsedCodes`, `SecurityAttempts`):
    - a database trigger makes the audit log append-only (error 50010), and `PlatformDbContext` refuses audit changes too;
    - scripts rehearsed (up `1|6|1|2|0|0`, idempotent, down clean).
  - **Server commands** (`dotnet InpremClockingApp.dll provider bootstrap|recover ...`) run before `app.Run()` and exit, never starting the web server:
    - `--dry-run` on all of them;
    - the code is asked for at a prompt when `--code` is left out;
    - output never shows a code, token or link.
  - **Provider pages** (area `Provider`: `/setup` on the Admin site, `/Provider/...` with `SiteMode=All`):
    - setup (404 once an owner exists or without `Bootstrap:CodeHash`);
    - login, authenticator code, recovery code (Level 1), logout;
    - accept invitation, two-factor enrolment (ten recovery codes shown once);
    - recovery approval (shows the request; approving needs a button);
    - console home with the "fewer than two owners" banner.
  - **Page protections:**
    - An access gate on the whole area: provider user, two-factor enrolled and used, security stamp checked on every request, so a recovery signs sessions out at once.
    - Antiforgery checked on Provider forms; `Referrer-Policy: no-referrer`.
    - Tenant accounts get no console and keep their App session.
    - `TenantContextMiddleware` skips Provider requests.
  - **`/health`** (Admin site only, on its host): Degraded below two active owners with two-factor.
  - **Changed files:** `Program.cs` (C2-2: provider registration, command dispatch, health endpoint), `Hosting/SiteRouting.cs` (console at the Admin site root), `Hosting/TenantContextMiddleware.cs`.
  - **Runbook:** `docs/multitenancy/runbooks/bootstrap-and-recovery.md`; its PowerShell code/hash snippet was checked against the app's hashing.
  - **Tests:**
    - Unit +39: every refusal path, dry runs, hashing, notifications, the Level 4 delay.
    - Integration +11 end to end: the setup-page journey (wrong code, invitation, enrolment, sign-in, Level 1), command bootstrap and Level 3, Level 4 with delay and `--complete`, Level 2, break-glass lock, append-only audit, no stored secrets.
    - The host-isolation test now allows the console at the Admin site root and `/health`.
    - Two access-gate mutations (no per-request stamp check, no re-enrol check) were caught by the tests.
  - **Results:** Unit 79, Architecture 11, Baseline 320, Integration 60, UI 5. No new advisories; no model drift in either context.
  - **Next:** Step 0.8, background jobs.

- **2026-10-13 — Phase 0 / Step 0.6 — Remove hardcoded credentials (C2-5).**
  - `IdentitySeeder` only ensures the `Admin` and `SuperAdmin` roles (SuperAdmin is still used by the old Platform pages and the login redirect). No account comes from `SeedAdmin:*` / `SeedSuperAdmin:*`, and those keys are gone from code, docs (`ROLES.md`, `Test.md`) and the test host.
  - Tests:
    - Architecture `NoSeedConfigurationTests` scans app and platform source/config for the keys.
    - Integration `NoSeededAccountsTests` records every configuration key the app reads during start-up, sign-in and a kiosk request. No seed key is read; with the keys set, no account is created; both roles exist.
    - Both tests were shown to fail against the old seeder.
  - Results: Unit 40, Architecture 11, Baseline 320, Integration 49, UI 5. No new advisories.
  - Next: Step 0.7, provider users, bootstrap and recovery.

- **2026-10-13 — Phase 0 / Step 0.5 — Isolation hardening (C2-1).**
  - **Fail-closed user filter.** No organization: no account visible. With one: its Active members, plus accounts of it without any membership row. It reads a read-only `platform.TenantMemberships` view mapped in `ApplicationDbContext`, excluded from migrations.
  - **Lookups by id, name and email** go through the new `Data/AppUserStore`, the one audited unfiltered path (registered in `AccountHosting`). The scattered user `IgnoreQueryFilters` in the Invitations page and `AuthService` are gone. `AuthService` lists `UserManager.Users` (= members).
  - **`Data/TenantSaveChangesInterceptor`** (one shared instance, registered in `OnConfiguring`) replaces the context's own stamping:
    - stamps Added rows, including EF's temporary key placeholders;
    - refuses Added rows for another organization;
    - for Modified/Deleted rows, reads the stored TenantId with plain SQL by primary key and refuses another organization's row;
    - never allows TenantId to change;
    - protects Tenant rows;
    - stamps new accounts.
    - Without an organization, only the no-change rule applies.
  - **Migration M4:**
    - alternate keys `(TenantId, StaffId)` / `(TenantId, VolunteerId)` and composite session foreign keys; cascade kept; fluent configuration only;
    - a guard (error 50004) stops it if any session crosses organizations;
    - scripts rehearsed: refused when crossed, then clean, idempotent; the DB rejects crossed inserts; reversible.
  - **F-08 fixed:** baseline test flipped (500, Beta unchanged); `baseline-findings.md` updated.
  - **Tests:**
    - New route-isolation suite: 94 routes and 646 requests with Beta ids (route, query, JSON and form bodies, every POST handler); no Beta data revealed, Beta fingerprint unchanged, every route exercised.
    - Proven to catch a cross-organization delete when the interceptor's check is disabled.
    - New coverage reflection tests (3) and save/filter rule tests (11).
    - Inspection tests updated (17 app migrations; crossed sessions can no longer be planted).
    - Results: Unit 40, Architecture 10, Baseline 320, Integration 48, UI 5. No new advisories. The local gitleaks run was stopped (slow); a credential-pattern check of the changed areas was clean, and CI scans on push.
  - Next: Step 0.6, remove hardcoded credentials.

- **2026-10-13 — Phase 0 / Step 0.4 follow-up — Dashboard admin count.**
  - `Pages/BackOffice.cshtml.cs` (C1, change approved by the developer): the Admins card counts `AuthService.GetAll()` (organization members) instead of `UserManager.Users` (accounts with this TenantId). 7 lines changed; line endings preserved.
  - New integration test: an invited member counts only after accepting. It fails on the old page and passes on the new one (both checked).
  - Tests: Unit 40, Architecture 7, Baseline 319 (dashboard figures unchanged for the seed), Integration 36, UI 5.

- **2026-10-13 — Phase 0 / Step 0.4 — Platform-unique email and login simplification.**
  - Migration **M3** (ApplicationDbContext, C2-1): `EmailIndex` becomes unique for non-null emails. A guard stops with error 50003 if emails are shared. Its scripts were rehearsed: refused with a duplicate and nothing changed; clean after the fix; idempotent; reversible. The per-tenant username indexes stay until the contract step.
  - C2-10: `TenantAwareUserValidator` replaced by `PlatformUserValidator` (user name and email unique platform-wide, past the query filter).
  - C2-3/C2-4: Login, ForgotPassword and ResetPassword use standard `SignInManager`/`UserManager` calls. The pick-by-password and one-link-per-account logic is gone; markup and messages are unchanged. All three left the architecture test's DbContext list.
  - C3-7: lockout after 5 failures for 15 minutes (`AccountHosting`).
  - C2-6/C3-6:
    - `AuthService.AddAdminAsync`: a new email gets an account plus an Active membership; an existing email gets an Invited membership (renewed if it was revoked) plus an invitation email. An existing member gets the same "already taken" error as before.
    - `Pages/User.cshtml.cs`: new confirmation message only (C3-6).
    - The Admins page lists Active members.
    - New `Areas/Account/Pages/Invitations` (accept/decline; reachable without an active membership).
  - Domain/Application: `Reinvite`, `Decline`; `InviteAsync`, `AcceptAsync`, `DeclineAsync`, `ListInvitationsAsync`, `AddMemberAsync`, `ListMembersAsync`; `IEmailSender`.
  - Tests:
    - Two `ChangedBy` baseline tests rewritten as the code comments explain: the same email in two organizations became an invitation; no lockout became a lockout after 5 failures. A same-organization duplicate test was added.
    - Inspection tests updated for 16 app migrations, and a shared email can no longer be planted.
    - Unit 40, Integration 35 (7 new: platform-wide duplicate, changed email, decline, re-invite revoked, invitation-only account, standard reset).
    - Results: Baseline 319, Architecture 7, UI 5, all passing. No new advisories. The local secret scan was cut short (low memory); CI runs it on push.
  - Next: Step 0.5, isolation hardening.

- **2026-10-13 — Phase 0 / Step 0.3 — Tenant context and memberships.**
  - **Domain:** `TenantMembership` (Owner|Admin; Invited|Active|Revoked) with its rules.
  - **Application:**
    - `ITenantContext` / `TenantContext` (set once per request) and `PlatformClaimTypes`.
    - `IMembershipStore` and `IClock` (interfaces).
    - `MembershipService`: per-request validation cached 5 minutes with `Invalidate`; sign-in selection with the legacy-account fallback.
    - `TimeZoneResolver` (IANA → Windows fallback).
  - **Infrastructure:**
    - `PlatformDbContext` gains `TenantMemberships` and a read-only `dbo.Tenants` view (excluded from migrations).
    - `MembershipStore` and `SystemClock`.
    - Migration **M2** creates the table and FKs to `AspNetUsers`/`Tenants`, then backfills. Its up/down/verify scripts were rehearsed with accounts: idempotent, reversible.
  - **Web:**
    - `TenantContextMiddleware`: validates `membership_id` + `tenant_id` every request, signs out otherwise, and sends people with several memberships to the chooser.
    - `TenancyHosting`: carries a chosen organization through security-stamp refresh.
    - C2-7: claims factory takes `tenant_id`/`membership_id` from the membership.
    - C2-8: `CurrentTenantService` reads `ITenantContext`.
    - C2-9: `TenantClock` and `CurrentTenantProfile` look up the tenant by id, with the time-zone fallback.
    - C3-5: `Areas/Account/Pages/ChooseOrganization` (new area; no DbContext; anti-forgery enforced).
    - `Program.cs` (C2-2): `AddTenancy`, `UseTenantContext`.
  - **Test seed:** memberships for every seeded admin (Owner/Admin).
  - **Tests:** Unit 31 (Domain 100% lines/branches, Application ~90% lines/97% branches), Architecture 7, Baseline 316 (single-membership admins unchanged), Integration 28. The 12 new membership tests cover the chooser, a foreign membership, a forged POST, revocation with and without cache invalidation, a revoked account, a legacy account, an operator, an account without an organization, and the M2 backfill, re-runnability and FKs. UI 5. No new advisories; no secrets in source.
  - Next: Step 0.4, platform-unique email and login simplification.

- **2026-10-13 — Phase 0 / Step 0.2 — Site mode and hostnames (C2-2).**
  - New in `InpremClockingApp/Hosting/`:
    - `SiteOptions`: `Site:SiteMode` App|Admin|All, `AppHost`, `AdminHost`, `PublicWebsiteOrigins`. Validated at startup; invalid settings stop the host.
    - `SiteRouting`: the Provider area belongs to Admin, everything else to App. Pages and controllers of an unserved site get no route (404); served ones get `HostAttribute` when a host is set.
    - `SiteHosting`: the cookie is `.Inprem.App` (SameSite Lax) or `.Inprem.Admin` (Strict); host-only, Secure, HttpOnly. Data-protection keys go to the DB (`PersistKeysToDbContext`), with application name `Inprem.App` / `Inprem.Admin`.
  - `Program.cs` (C2-2): file-system key storage replaced by `AddPlatformInfrastructure` + `AddSiteHosting`. `appsettings.json` (C2-11): `Site` section with `SiteMode: App`.
  - Infrastructure: `PlatformDbContext` (schema `platform`, D3) with `DataProtectionKeys`, and migration `M1a_DataProtectionKeys` (additive). Scripts in `scripts/migrations/` (idempotent up/down + verify), rehearsed on LocalDB: up, up again, down, down again. No model drift in either context.
  - Packages: EF Core 8.0.0 (same as the app). `System.Security.Cryptography.Xml` pinned to 8.0.4, because DataProtection 8.0.0 brought in 8.0.0 (GHSA-w3x6-4m5h-cxqf, high). The package gate caught it.
  - Tests: new host-isolation suite (Integration, 16):
    - App mode has no provider route.
    - Admin mode returns 404 for every one of the App site's routes, GET and POST (enumerated from the endpoint table).
    - All mode serves both.
    - Host restriction works on the right, wrong and unknown host.
    - Cookie attributes are correct per site, including on a real login response.
    - Keys are shared between App instances through the DB, and the Admin site cannot read App data.
    - Invalid settings stop startup.
  - Test support: `TestDatabase` migrates both contexts; the factory takes settings and an option to keep the real keys.
  - Tests: Architecture 7/7, Integration 16/16, Baseline 316/316 (with SiteMode=App), UI 5/5; no new advisories; no secrets.
  - Next: Step 0.3, tenant context and memberships.

- **2026-10-13 — Phase 0 / Step 0.1 — Solution structure.**
  - Added `src/Inprem.Platform.Domain`, `.Application` and `.Infrastructure` (net8.0, nullable, warnings as errors via `src/Directory.Build.props`), with marker types only. References: Application → Domain; Infrastructure → Application, Domain.
  - `InpremClockingApp.csproj`, an unlisted existing file approved by the developer, now references Application and Infrastructure. The app's 167 resolved packages are identical before and after.
  - Architecture tests (NetArchTest + reflection, 7):
    - Domain has no EF Core, ASP.NET Core or outer-layer dependency.
    - Application has no Infrastructure, web, EF Core or ASP.NET Core dependency.
    - Infrastructure does not depend on the web project.
    - Project references point inwards.
    - No page model or controller uses a DbContext, except 13 approved-app types listed by name. A further test fails if any of those stops using one, so the list only shrinks deliberately.
  - Each rule was shown to fail on a deliberate violation (a new page with a DbContext; EF Core in Domain), then reverted. Circular references, the other way to break the rules, are rejected by the build.
  - Tests: Architecture 7/7, Baseline 316/316, UI 5/5; package gate shows no new advisories.
  - Next: Step 0.2, site mode and hostnames.

- **2026-10-13 — G0 complete; `mt/g0` merged into `mt/integration` (`ee940f8`)** at the developer's request, after review: clean Release build, Baseline 316/316, UI 5/5, no new vulnerable packages, no new secrets. The developer clarified that the inspected backup was the test deployment (corrected since; not going live), so `inspection-results.md` was relabelled.

- **2026-10-13 — G0-2 — Inspection of the supplied backup (later identified as the test deployment).**
  - Restored `db_aa61e6_inpremdb3_10_5_2026_8.bak` (SQL Server 2022) in a throwaway local SQL Server 2022 container, ran 01–03, then deleted the container.
  - Added `04p`/`05p` pre-tenancy variants, because production has no `TenantId` yet. Their refusal tests are in the suite.
  - Results, counts only, are in `docs/multitenancy/inspection-results.md`. No personal data was displayed or recorded.

- **2026-10-13 — G0 / Step 5 — Draft website API description.**
  - `docs/multitenancy/api/public-v1.draft.yaml` (OpenAPI 3.0.3), marked DRAFT, written from design section 7. It covers:
    - all 17 endpoints of 7.3;
    - `signupToken` and `billingAccessToken` (bearer) and optional `X-Api-Key`;
    - the `Idempotency-Key` header;
    - ProblemDetails with 20 stable error codes, and 429 with `Retry-After`;
    - checkout statuses (7.5) and subscription statuses (8).
  - Validated with Spectral 6.14.2 (`spectral:oas`, no warnings) and Redocly 1.25.11 (valid; 1 warning, no licence chosen). Both caught real YAML errors, which were fixed. `docs/multitenancy/api/README.md` explains how to re-run them.
  - G0 exit checks:
    - G0-4: done.
    - CI on every PR: done.
    - OpenAPI validates: done.
    - App diff: only the new `AssemblyInfo.Tests.cs`.
    - G0-2: waiting for the developer's run on a fresh backup.

- **2026-10-13 — G0 / Step 4 — Database inspection scripts.**
  - Added `scripts/inspection/01-05*.sql` and a README.
    - All five scripts are read-only, and each refuses to run unless `-v ConfirmLocalCopy=YES` is passed.
    - 04 starts with a counts-only summary; its detail results stay local.
    - The README covers how to restore a backup, run the scripts with sqlcmd, record results and check model drift.
  - Tests: 11 new `Inspection` tests run the scripts against the seeded database (refusal, migrations 15/15, row counts, a clean seed, R1–R3 totals). A planted-problems test checks each problem type is reported. Also run with real sqlcmd on a migrated LocalDB database.
  - `docs/multitenancy/inspection-results.md` is a template until the real run.
  - CI on GitHub: all three jobs green (run 38326059916).
  - Next: Step 5, draft website API description.

- **2026-10-12 — G0 / Step 3 — CI pipeline.**
  - `.github/workflows/ci.yml` runs on PRs and pushes to `mt/**`. Three jobs, no deployment:
    - **Build and test:** restore, Release build (warnings are errors under `tests/`), all suites against a SQL Server service container (`INPREM_TEST_DB=external`), Playwright browser tests, trx results (dorny/test-reporter), coverage (coverlet + ReportGenerator summary), artifacts uploaded.
    - **Secret scan:** gitleaks v8.21.2 over the whole history with `.gitleaks.toml` (defaults + two custom rules) and `.gitleaksignore`.
    - **Vulnerable packages:** `scripts/ci/check_vulnerable_packages.py` fails on any high/critical advisory not in `scripts/ci/known-vulnerable-packages.txt`.
  - Testcontainers 4.1.0 → 4.16.0, which removes the vulnerable SSH.NET it pulled in.
  - Checked locally: gitleaks command (no leaks beyond the one listed); package gate (exit 0, and exit 1 when a known line is removed); actionlint clean; Linux mirror of build-and-test passed (Baseline 303/303, UI 5/5).
  - Not yet: a real run on GitHub.
  - Next: Step 4, database inspection scripts.

- **2026-10-12 — G0 / Step 2 — Behaviour baseline suite.**
  - **Baseline (303 tests, all passing on unchanged code):**
    - Kiosk: clock in, break, clock out and working hours, with every rule message; one session per day; organization-local `ClockDate`; registration with same-name and duplicate checks and category rules; search.
    - Back office: every page loads with the right content; staff, volunteer and clocking create/edit/move/delete; manual clocking entry and edit rules; admin accounts.
    - Reports: totals for fixed ranges (dashboard, Hours Worked, hours API) and text of every PDF (PdfPig), including the clock-change days in local time; CSV, Excel, PDF and JSON exports.
    - Access rules: anonymous, signed-in without a role, Admin, and SuperAdmin-only pages.
    - Isolation as today: reads on every page, API and PDF; writes checked by fingerprinting Beta's rows.
  - **UI (Playwright, 5 tests, all passing):** kiosk clock buttons with their toast messages and redirects, volunteer clock-in, kiosk registration, and the Manage Staff "Add New" dialog.
  - **Findings:** 9 found (F-01 to F-09), one of them a cross-tenant delete (F-08). Details in `docs/multitenancy/baseline-findings.md`.
  - **Test databases:** suite passes on LocalDB (twice) and on a SQL Server container.
  - `git diff 1061ea8 -- InpremClockingApp/` still shows only `Properties/AssemblyInfo.Tests.cs`.
  - Next: Step 3, CI pipeline.

- **2026-10-12 — G0 / Step 1 — Test infrastructure.**
  - Added test projects: `InpremClockingApp.Tests.Support` (shared harness), `InpremClockingApp.Tests.Baseline`, `InpremClockingApp.Tests.Ui`, and empty `Inprem.Platform.Tests.Unit`, `.Integration` and `.Architecture`. `tests/Directory.Build.props` makes warnings errors for all of them.
  - Harness: `TestSqlServer` provisions the database, chosen by `INPREM_TEST_DB` (`localdb`, `container` or `external`). `AppFixture` creates a fresh database, migrates it, starts the unchanged app and seeds data; the database is dropped on dispose. `KestrelAppFixture` adds a real port for Playwright. `SignIn` posts the real login form.
  - Seed data (`TestDataSeeder`/`SeedCatalog`): Alpha (America/New_York) and Beta (Europe/London) each have an owner, an admin, 3 staff and 3 volunteers, with sessions on 4 days including the spring daylight-saving change (8 Mar / 29 Mar 2026). Gamma is empty. The admin password is generated per run.
  - Tests: Baseline 8/8 passed (infrastructure: app starts, seed counts, DST rows stored in UTC, no configuration-seeded accounts, login redirect to `/VolunteerAttendance`, wrong password, anonymous redirect). UI 1/1 passed (`--filter Category=Ui`; plain `dotnet test` skips it). Platform projects have no tests yet.
  - Next: Step 2, the behaviour baseline suite.
