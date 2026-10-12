# 02 — Phase 0: Foundation

**Entry:** Phase G0 exit checks pass (baseline suite green, CI running, inspection done). Decisions D1–D17 and C3-5, C3-6, C3-7 from `DECISIONS.md`.
**Branch:** `mt/phase0` from `mt/integration`.
**Design references:** sections 2, 3, 4, 5, 10, 11, 12, 13 (Phase 0).

Follow `00-guardrails.md`. Run all required suites (`TESTING.md`) after every step; update `PROGRESS.md`.

## Step 0.1 — Solution structure

Create `Inprem.Platform.Domain`, `Inprem.Platform.Application`, `Inprem.Platform.Infrastructure`, `Inprem.Platform.Tests` (all `net8.0`, nullable enabled). Reference rules: Application → Domain; Infrastructure → Application, Domain; web project → Application, Infrastructure. Add an architecture test (NetArchTest or equivalent) that fails if Domain references EF Core or ASP.NET, if Application references Infrastructure, or if any new page/endpoint class references `DbContext`.

## Step 0.2 — Site mode and hostnames (C2-2)

- Options `SiteOptions { SiteMode: App | Admin | All; AppHost; AdminHost; PublicWebsiteOrigins[] }` validated at startup.
- Approved pages, `Areas/Billing`, website API and webhooks are mapped only when `SiteMode` is `App` or `All`, restricted with `RequireHost(AppHost)` when hosts are configured. `Areas/Provider` only for `Admin` or `All`, restricted to `AdminHost`. Wrong-site requests return 404.
- Authentication cookies: host-only (no `Domain`), distinct names per site (`.Inprem.App`, `.Inprem.Admin`), `Secure`, `HttpOnly`, `SameSite=Lax` (App) / `Strict` (Admin).
- Data-protection keys persisted to the database (`PersistKeysToDbContext`) with application name per site.
- **Tests (host-isolation suite):** with `SiteMode=App`, every provider route → 404; with `SiteMode=Admin`, every back-office, kiosk, API and webhook route → 404; cookies are host-only; behaviour baseline passes with `SiteMode=App`.

## Step 0.3 — Tenant context and memberships

- Domain: `TenantMembership { Id, UserId, TenantId, Role (Owner|Admin), Status (Invited|Active|Revoked), InvitedByUserId, CreatedUtc }`.
- New `PlatformDbContext` (D3) on the same database, schema `platform`; migrations in Infrastructure.
- Migration **M2**: create `TenantMemberships`; backfill one Active membership per `AspNetUsers` row with non-null `TenantId` (role Admin; mark the earliest admin of each organization as Owner; the Provider Console can change ownership later). Produce idempotent and down scripts and verification queries.
- `ITenantContext` (Application) implemented by a middleware that reads `tenant_id` and `membership_id` claims, re-checks the membership is Active and the tenant not Closed (5-minute memory cache, invalidated on change), and signs the user out otherwise.
- C2-7: claims factory adds `membership_id`; C2-8: `CurrentTenantService` reads from `ITenantContext`; C2-9: `TenantClock` and `CurrentTenantProfile` read the tenant from `ITenantContext`, add Windows time-zone fallback (`TimeZoneInfo.TryConvertIanaIdToWindowsId`).
- C3-5: new page "Choose organization" (App site), shown only when a user has more than one Active membership.
- **Tests:** single-membership admins sign in exactly as before (baseline unchanged); multi-membership users see the chooser; revoked membership signs out on next request.

## Step 0.4 — Platform-unique email and login simplification

- Run the inspection query for duplicate `NormalizedEmail`. If any exist, **stop** and list them; the developer decides how to resolve each (rename test accounts or merge into one person with two memberships) before the index is added.
- Migration **M3**: unique index on `AspNetUsers.NormalizedEmail` (filtered for non-null); keep the existing per-tenant username indexes until the contract step.
- C2-10: replace `TenantAwareUserValidator` with platform-unique email validation.
- C2-3/C2-4: remove the pick-account-by-password logic from `Login.cshtml.cs`, `ForgotPassword.cshtml.cs`, `ResetPassword.cshtml.cs`; use standard `SignInManager` / `UserManager` calls. Keep page markup, fields, messages and the default redirect for single-organization admins identical.
- C3-7 (On): `lockoutOnFailure: true`, 5 attempts, 15 minutes.
- C2-6 / C3-6: `AuthService.CreateAdmin` creates a membership in the current organization; if the email already has an account, create an `Invited` membership and send an invitation instead of a duplicate account (new confirmation message).
- **Tests:** baseline unchanged; duplicate email rejected; existing-email admin creation yields invitation; lockout (if enabled).

## Step 0.5 — Isolation hardening (C2-1)

- `AppUser` query filter fails closed: when no tenant is set, no users are visible (provider and login lookups use explicit, audited queries in Infrastructure, not `IgnoreQueryFilters` scattered in pages).
- `TenantSaveChangesInterceptor`: stamps `TenantId` on Added; throws on Modified/Deleted rows whose original `TenantId` differs from the current tenant; prevents `TenantId` changes.
- Migration **M4** (after the inspection query shows zero cross-tenant clockings): alternate keys `(TenantId, StaffId)` on `Staffs` and `(TenantId, VolunteerId)` on `Volunteers`; replace the clocking foreign keys with composite keys `(TenantId, StafId) → Staffs(TenantId, StaffId)` and `(TenantId, VoluntId) → Volunteers(TenantId, VolunteerId)`. Configure entirely in `ApplicationDbContext` fluent API (`HasAlternateKey`, `HasPrincipalKey`); **do not edit model classes** (they are C1).
- **Tests (tenant-isolation suite):** discover every route from `EndpointDataSource`; for each route with an id parameter, Tenant A requests Tenant B's ids → 403/404 and no Tenant B row changes (compare row hashes before/after). Query-filter coverage reflection test: every entity with a `TenantId` has a filter and is covered by the interceptor.

## Step 0.6 — Remove hardcoded credentials (C2-5)

- `IdentitySeeder`: keep role creation (`Admin`, `SuperAdmin` if still referenced); remove creation of any account from `SeedAdmin:*` or `SeedSuperAdmin:*`. Remove those keys from documentation and configuration.
- Secret-scan and a test that fails if configuration keys named `SeedAdmin` or `SeedSuperAdmin` are read anywhere.

## Step 0.7 — Provider users, bootstrap and recovery (design section 5)

- Domain: `ProviderUser { UserId, Role (Owner|Billing|Support), Status }`, `Invitation { Id, Email, Purpose, TokenHash, ExpiresUtc, ConsumedUtc, CreatedBy }`, `RecoveryRecord`, `AuditEntry` (append-only).
- Console command host inside the web project (`dotnet InpremClockingApp.dll provider <command>`) that runs before `app.Run()` and exits:
  - `provider bootstrap --email <e> --code <c> [--dry-run]`: refuses if any Provider Owner exists, if the code (compared against the hash stored in the secret store setting `Bootstrap:CodeHash`) is wrong or already used; creates the user without password + 24-hour invitation; marks the code used; writes audit; never prints the link.
  - `provider recover --user <existing owner email> --code <break-glass code> --approver <email from Security:RecoveryApprovers> [--dry-run]` (Level 3): sends an approval link to the approver (1 hour); after approval, resets password and two-factor, revokes sessions (security stamp), sends a 1-hour invitation to the **existing verified email only**; rate-limited to one per account per 24 hours; lock after repeated failures.
  - `provider recover --replace --old <email> --new-email <email> --code <c> --approver <email> --approval-ref <text>` (Level 4): same controls plus a 24-hour delay unless both approvers waive; disables the old owner.
- **One-time setup page (D13, for hosts without a console such as SmarterASP):** `/setup` on the Admin site (or `/provider/setup` with `SiteMode=All`), available only while no Provider Owner exists and `Bootstrap:CodeHash` is configured, otherwise 404. Asks for the bootstrap code and owner email, then does exactly what `provider bootstrap` does (shared use case). Rate-limited; every attempt audited.
- Provider login on the Admin site requires two-factor enrolment before any console page (authenticator app via Identity; recovery codes shown once).
- Level 2 peer reset is implemented in Phase 1 (console UI); add the use case and tests now.
- Every bootstrap/recovery action: audit entry and notification email to all Provider Owners and `Security:Mailbox`.
- Go-live check: console banner and a health check warn while fewer than two active Provider Owners exist.
- Write the runbook `docs/multitenancy/runbooks/bootstrap-and-recovery.md` (step-by-step for the operator and the approver, both the command and the setup-page routes, including the six-monthly drill). The developer, not Claude Code, executes it on a test site.
- **Tests:** every refusal path; codes stored only as hashes; sessions revoked; notifications sent; dry-run changes nothing.

## Step 0.8 — Background jobs

- Hangfire with SQL Server storage (`hangfire` schema). Job server only when `Jobs:RunServer=true` (App site). Admin site registers the dashboard read-only, protected by the Provider Owner role.
- Remove `BillingBackgroundService` registration (C2-2). Leave the C4 billing files in place until Phase 2 replaces them.
- **Tests:** Admin site starts no job server; App site does.

## Step 0.9 — (moved)

Anti-forgery (C3-10) is done in Phase 7, once the UI suite covers every form.

## Step 0.10 — Migrations M1 (platform tables shell) and deployment rehearsal

- Migration **M1**: create empty platform tables needed in Phase 0 (`ProviderUsers`, `Invitations`, `AuditEntries`, `RecoveryRecords`) in schema `platform`.
- Follow design section 11 on a disposable copy: generate idempotent scripts for M1–M4, add guards (stop on error, expected last migration), rehearse apply and rollback, record timings in `docs/multitenancy/rehearsal-phase0.md`.
- Add a pipeline job (no deployment) that produces one build artifact and a deployment script that takes `SiteMode` per target, to be used by whoever deploys.

## Acceptance criteria (design section 13)

| ID | Criterion | Measure |
|---|---|---|
| P0-1 | Approved behaviour preserved | Baseline suite 100%; report totals identical |
| P0-2 | Tenant isolation | 100% of routes covered; 0 failures; 0 foreign rows changed |
| P0-3 | Filter coverage | 100% of tenant-owned entities |
| P0-4 | Memberships | Every tenant admin has exactly one Active membership; no visible change for single-org admins |
| P0-5 | No credentials | Secret scan 0 findings; no `SeedAdmin`/`SeedSuperAdmin` reads |
| P0-6 | Bootstrap and recovery | Safeguards tested automatically for command and setup page; runbook written; developer executes bootstrap and Levels 1–3 on a test site and records it in `PROGRESS.md` |
| P0-7 | Security baseline | 0 high/critical vulnerable packages; lockout test (if C3-7) |
| P0-8 | CI | All suites on every PR; median under 15 minutes |
| P0-9 | Migrations M1–M4 | Rehearsed with rollback on a copy |
| P0-10 | Code quality | Coverage targets in `TESTING.md` met; no new warnings |
| P0-11 | Two-site deployment | Pipeline produces one artifact and per-site deployment script; developer deploys to App and Admin staging; host-isolation 0 failures; jobs only on App |
| P0-12 | Approved app on new host | Baseline 100% against the `app.` staging hostname |

## Out of scope

Provider Console screens (Phase 1), PayPal (Phase 2), website API (Phase 3), Twilio (Phase 5), removing C4 files, contract migrations (M5/M6), DNS or production deployment.
