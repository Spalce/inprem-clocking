# 00 — Guardrails for every Claude Code session

You are building the multi-tenancy layer of **InpremClockingApp** (ASP.NET Core 8 Razor Pages + Web API, EF Core 8, SQL Server, ASP.NET Core Identity). Read these files at the start of **every** session:

1. this file;
2. `DECISIONS.md`: settled choices (they override any "approval" or "open question" wording in the design);
3. `TESTING.md`: what must be tested and run;
4. `PROGRESS.md`: where the work stands;
5. the current phase file, and only the design sections it lists in `docs/multitenancy/design-v0.5.md`.

There are no approval gates. A phase starts when the previous phase's exit checks pass (recorded in `PROGRESS.md`). The developer decides when to continue.

## 1. Baseline and branches

- Approved application: branch `fix/login-admin-only`, commit `1061ea8`. Its behaviour (kiosk, back office, reports, PDFs) must be preserved.
- Integration branch: `mt/integration`. Each phase on its own branch from it: `mt/g0`, `mt/phase0`, `mt/phase1`, ... Small commits with clear messages.
- Never push to, merge into or rebase `main` or `fix/login-admin-only`. The developer merges phase branches into `mt/integration`.
- Never deploy. Never run migrations except against a local or disposable test database.

## 2. Session workflow

1. Read the files above; state the current phase and the next unfinished step.
2. Show a short plan for **that step only** (files to add or change, with the category of each existing file) and wait for "go".
3. Implement; write or update tests in the same step.
4. Run the required suites (`TESTING.md`); fix failures; never skip, weaken or delete tests.
5. Update `PROGRESS.md`; report in the format in section 9.
6. If anything in the code differs from the prompt or design, **stop and ask**.

## 3. Change control for existing files (design section 10)

**C1 — Frozen. Do not modify, reformat, rename or move:**
- `InpremClockingApp/Pages/**` except `Pages/Shared/_Sidebar.cshtml`, `Pages/Shared/_MainLayout.cshtml` (C3) and `Pages/Platform/**` (C4).
- Controllers: `ControlsController`, `PeopleController` (both files), `SearchController`, `StaffController`, `ReportsController`, `Api/StaffReportsController`, `Api/VolunteerReportsController`.
- Services: `StaffService`, `VolunteerService`, `StaffClockingService`, `VolunteerClockingService`, `EmailService`.
- Models `Staff`, `Volunteer`, `Clocking`, `ClockingStaff`, view models; helpers `PdfTableStyle`, `PrivacyMask`, `RosterExport`, `VolunteerCategoryValidation`, `WorkingHoursFormat`.
- `wwwroot/**` (new assets go under new folders such as `wwwroot/provider/`, `wwwroot/billing/`, `wwwroot/demo-site/`).
- `Areas/Identity/**` except the three files in C2-3/C2-4.
- Existing migrations up to `20261003091001_AddVolunteerContactPersonDetails`: never edited; only new migrations are added.

**C2 — Internal change only (users see no difference):**

| ID | File | Allowed change |
|---|---|---|
| C2-1 | `Data/ApplicationDbContext.cs` | Fail-closed user filter; save interceptor registration; composite keys; platform-unique email index |
| C2-2 | `Program.cs` | Register new projects, `SiteMode` and host mapping, tenant-context middleware, Hangfire, website API, CORS, rate limiting, data-protection storage, host-only cookies, global filters; remove `BillingBackgroundService` |
| C2-3 | `Areas/Identity/Pages/Account/Login.cshtml.cs` | Remove pick-account-by-password; standard sign-in; chooser redirect only for more than one membership |
| C2-4 | `ForgotPassword.cshtml.cs`, `ResetPassword.cshtml.cs` | Remove multi-account logic |
| C2-5 | `Helpers/IdentitySeeder.cs` | Remove `SeedAdmin` / `SeedSuperAdmin` account creation; keep roles |
| C2-6 | `Services/AuthService.cs` | `CreateAdmin` also creates a membership |
| C2-7 | `Services/AppUserClaimsPrincipalFactory.cs` | Add `membership_id`; `tenant_id` from membership |
| C2-8 | `Services/CurrentTenantService.cs` | Read from validated tenant context |
| C2-9 | `Services/TenantClock.cs`, `Services/CurrentTenantProfile.cs` | Use `ITenantContext`; Windows time-zone fallback |
| C2-10 | `Helpers/TenantAwareUserValidator.cs` | Replace with platform-unique email validation |
| C2-11 | `appsettings.json` | Non-secret settings only |

**C3 — Visible change:** allowed only as set in `DECISIONS.md` (each C3 item there is On, Off or Behind a setting).

**C4 — Replaced:** `Pages/Platform/**`, `Controllers/Api/InvoicesController.cs`, `Services/TenantAdminService.cs`, `Services/BillingService.cs`, `Services/BillingBackgroundService.cs`, `Models/Billing/*`. Remove only in the step that says so, after the replacement passes its tests.

## 4. Architecture rules for new code

- Projects: `Inprem.Platform.Domain`, `Inprem.Platform.Application`, `Inprem.Platform.Infrastructure`; tests in `tests/`. Dependencies point inwards (Web → Application → Domain; Infrastructure → Application, Domain). An architecture test enforces this.
- Thin pages and endpoints: one use case per handler; no `DbContext` in new pages or endpoints.
- Interfaces for outside services: `IPaymentProvider`, `ISmsSender`, `IOtpService`, `IEmailSender`, `IClock`, `IAuditLog`, `ITenantContext`.
- DTOs separate from entities; FluentValidation; typed options with `ValidateOnStart`.
- API errors as `ProblemDetails` with stable error codes.
- Structured logs with tenant id and correlation id; never log passwords, tokens, full emails, phone numbers or payment payloads.
- **UI rule: no create or edit form on a list page.** List → detail → separate create/edit page; Post/Redirect/Get; pop-ups only to confirm destructive actions.

## 5. Secrets and security

- No credentials, connection strings, API keys or account emails/passwords in code, `appsettings*.json`, tests, migrations or the repository. Local: `dotnet user-secrets`. Server: host configuration or an uncommitted `appsettings.Production.json`.
- No seeded accounts with passwords. Provider accounts come only from bootstrap (command or one-time setup page) or invitations.
- PayPal **sandbox** and Twilio **test/trial** credentials only. Live mode requires an explicit setting that no step in this pack turns on.

## 6. Database rules (design section 11)

- Migrations are additive unless a step explicitly asks for a contract migration.
- For every migration: idempotent script (`dotnet ef migrations script <from> <to> --idempotent`) under `scripts/migrations/`, a reverse script, and verification queries.
- Never apply migrations automatically at startup.
- Run `dotnet ef migrations has-pending-model-changes` for each DbContext before finishing a step that touches the model.

## 7. Definition of done for a step

- Builds with no new warnings in new projects.
- All suites required by `TESTING.md` for the current phase pass, including the behaviour baseline.
- New code covered by tests (see coverage targets in `TESTING.md`).
- `PROGRESS.md` updated.

## 8. Stop and ask when

- A change would touch a C1 file, or a C3 item that `DECISIONS.md` sets to Off.
- A behaviour-baseline test fails.
- The design, the prompt and the code disagree, or something is ambiguous.
- A secret, an external account or access is needed.
- A step would delete data or drop a table outside a contract-migration step.

## 9. Report after every step

```
Phase / step: <e.g. Phase 1 / 1.2>
Files changed: <path — new / C2-x / C3-x / C4>
Tests: <suite: passed/failed counts>
Exit checks met so far: <IDs>
Shortcuts or follow-ups: <or none>
Next step: <one line>
```
