# Migration scripts

Migrations are never applied when the app starts (decision D12). Every migration is shipped here as reviewed SQL:

- an idempotent **up** script;
- an idempotent **down** script;
- **verification** queries (read-only).

Whoever deploys runs them, in order, before the new build goes live. Back up the database first.

| Order | Migration | Context | Up | Down | Verify | Notes |
|---|---|---|---|---|---|---|
| 1 | `20261013080438_M1a_DataProtectionKeys` (Phase 0, step 0.2) | `PlatformDbContext` | `platform-M1a-up.idempotent.sql` | `platform-M1a-down.idempotent.sql` | `platform-M1a-verify.sql` | Additive: schema `platform`, its own `__EFMigrationsHistory`, table `DataProtectionKeys`. **Must be applied before the step 0.2 build is deployed**: the app reads and writes its cookie keys there. |

## Running

```powershell
sqlcmd -S <server> -d <database> -E -b -i scripts/migrations/platform-M1a-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -b -i scripts/migrations/platform-M1a-verify.sql   # expect 1|1|<rows>
```

The scripts are safe to run twice. Down reverses up. Rehearsed on 2026-10-13 against a throwaway LocalDB database: verify gave before `0|0`, after up `1|1|0`, after a second up `1|1|0`, after down `0|0`, after a second down `0|0`.

## What the step 0.2 deployment changes for users

- **Everyone is signed out once.** The sign-in cookie is renamed (`.AspNetCore.Identity.Application` → `.Inprem.App`), and the keys that protect it move from `App_Data/DataProtection-Keys` (files) to `platform.DataProtectionKeys`. Old cookies can no longer be read, so each user signs in again once. Nothing else changes for them.
- **Configuration per site:**
  - `Site:SiteMode` must be set. `appsettings.json` has `App`.
  - `Site:AppHost` / `Site:AdminHost` restrict each site to its hostname. They are empty in `appsettings.json` (any host), so set them per environment at the hostname cutover (`docs/multitenancy/build/09-domain-cutover.md`).
- **The old key files in `App_Data/DataProtection-Keys` are no longer used.** They can be deleted from the server after the deployment.

## Regenerating

Use EF Core 8 tools, not the 7.0.2 version pinned in `InpremClockingApp/.config/dotnet-tools.json` (see `scripts/inspection/README.md`):

```powershell
cd src/Inprem.Platform.Infrastructure
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script 0 M1a_DataProtectionKeys --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1a-up.idempotent.sql
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M1a_DataProtectionKeys 0 --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1a-down.idempotent.sql
```
