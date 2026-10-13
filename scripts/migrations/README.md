# Migration scripts

Migrations are never applied when the app starts (decision D12). Every migration is shipped here as reviewed SQL:

- an idempotent **up** script;
- an idempotent **down** script;
- **verification** queries (read-only).

Whoever deploys runs them, in order, before the new build goes live. Back up the database first.

| Order | Migration | Context | Up | Down | Verify | Notes |
|---|---|---|---|---|---|---|
| 1 | `20261013080438_M1a_DataProtectionKeys` (Phase 0, step 0.2) | `PlatformDbContext` | `platform-M1a-up.idempotent.sql` | `platform-M1a-down.idempotent.sql` | `platform-M1a-verify.sql` | Additive: schema `platform`, its own `__EFMigrationsHistory`, table `DataProtectionKeys`. **Must be applied before the step 0.2 build is deployed**: the app reads and writes its cookie keys there. |
| 2 | `20261013083152_M2_TenantMemberships` (Phase 0, step 0.3) | `PlatformDbContext` | `platform-M2-up.idempotent.sql` | `platform-M2-down.idempotent.sql` | `platform-M2-verify.sql` | Additive. Creates `platform.TenantMemberships`, with foreign keys to `dbo.AspNetUsers` (cascade) and `dbo.Tenants`. **Backfills** one Active membership per account that has an organization; the only admin of an organization becomes Owner, and organizations with several admins get Admins only (verify lists them, so someone can name the Owner). **Must be applied before the step 0.3 build is deployed**: without a membership, an admin is signed out. Down drops the table, and memberships with it. |
| 3 | `20261013161410_M3_PlatformUniqueEmail` (Phase 0, step 0.4) | `ApplicationDbContext` (dbo, the approved app's own history table) | `app-M3-up.idempotent.sql` | `app-M3-down.idempotent.sql` | `app-M3-verify.sql` | Makes `AspNetUsers.EmailIndex` unique (non-null emails): one account per email platform-wide. **Guard:** stops with error 50003 and changes nothing if two accounts share an email (list them with `scripts/inspection/04-data-quality.sql`, result 2). Down restores the non-unique index. |
| 4 | `20261013175341_M4_CompositeTenantKeys` (Phase 0, step 0.5) | `ApplicationDbContext` | `app-M4-up.idempotent.sql` | `app-M4-down.idempotent.sql` | `app-M4-verify.sql` | Alternate keys `(TenantId, StaffId)` and `(TenantId, VolunteerId)`. The session foreign keys become composite, so a session can only point at a person of its own organization; delete still cascades. **Guard:** stops with error 50004 and changes nothing if a session already belongs to another organization than its person (`scripts/inspection/04-data-quality.sql`, result 4). Down restores the single-column keys. |
| 5 | `20261013185539_M1b_ProviderSecurity` (Phase 0, step 0.7) | `PlatformDbContext` | `platform-M1b-up.idempotent.sql` | `platform-M1b-down.idempotent.sql` | `platform-M1b-verify.sql` | Additive (part of design M1, "platform tables"; runs after M2 in the platform history). Tables `ProviderUsers`, `ProviderInvitations`, `RecoveryRecords`, `AuditEntries`, `UsedCodes`, `SecurityAttempts`; foreign keys from provider users and invitations to `dbo.AspNetUsers` (no cascade). Trigger `TR_AuditEntries_AppendOnly`: any UPDATE or DELETE of an audit entry fails (error 50010). Codes and link tokens are stored only as SHA-256 hashes. **Must be applied before the step 0.7 build is deployed** (the Provider pages and the `provider` commands use these tables). Down drops the tables, and the audit log with them: export it first if it matters. |

## Running

```powershell
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1a-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1a-verify.sql   # expect 1|1|<rows>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M2-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M2-verify.sql   # expect 1|<n>|0|<orgs without owner>
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M3-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M3-verify.sql        # expect 1|1|0
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M4-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/app-M4-verify.sql        # expect 1|2|2|0
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1b-up.idempotent.sql
sqlcmd -S <server> -d <database> -E -I -b -i scripts/migrations/platform-M1b-verify.sql  # expect 1|6|1|2|<owners>|<open recoveries>
```

Always pass `-I` (`QUOTED_IDENTIFIER ON`). `sqlcmd` turns it off by default, and statements touching `dbo.AspNetUsers` fail without it, because that table has filtered indexes.

The scripts are safe to run twice. Down reverses up. All rehearsals ran on 2026-10-13 against throwaway LocalDB databases.

- **M1a:** verify gave before `0|0`, after up `1|1|0`, after a second up `1|1|0`, after down `0|0`, after a second down `0|0`.
- **M3:** the database had the approved app's 15 migrations and two accounts sharing an email. Verify gave before `0|0|1`. Up was refused (error 50003, exit 1) and verify was still `0|0|1`. After renaming one account, up gave `1|1|0`, a second up the same, and down `0|0|0`.
- **M4:** the database had migrations up to M3 and one staff session filed under another organization than its person. Verify gave before `0|0|0|1`. Up was refused (error 50004, exit 1) and verify was unchanged. After correcting the session, up gave `1|2|2|0`, and a second up the same. A new crossed session was then rejected by the database (`FK_ClockingsStaff_Staffs_TenantId_StafId`). Down gave `0|0|0|0`.
- **M1b:** the database had the app's migrations and platform M1a and M2. Verify gave before `0|0|0|0`; after up `1|6|1|2|0|0`; after a second up the same. An UPDATE of an audit entry failed with the append-only error. Down gave `0|0|0|0`, a second down the same, and the platform history kept M1a and M2.
- **M2:** the database had the app's migrations, two organizations (one admin; two admins) and one operator account without an organization. Verify gave before `0`; after up `1|3|0|1`, listing the organization with 2 admins; after a second up the same; after down `0`; after up again `1|3|0|1`.

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
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M2_TenantMemberships M1b_ProviderSecurity --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1b-up.idempotent.sql
& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M1b_ProviderSecurity M2_TenantMemberships --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1b-down.idempotent.sql
```
