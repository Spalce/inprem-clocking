# 01 — Phase G0: Baseline and preparation

**Branch:** `mt/g0` from `mt/integration` (which starts at `fix/login-admin-only`, commit `1061ea8`).
**Entry:** none — this is the first phase.
**Allowed:** add new projects, test code, CI workflow, read-only SQL scripts and documentation. **Not allowed:** any change to existing application files (C1, C2, C3, C4), any migration, any shared or live database.
**Tip:** take a backup of the hosted database from the hosting control panel and restore it locally for Step 4.
**Design references** (in `docs/multitenancy/design-v0.5.md`): sections 11.1, 12.1, 13 (gate G0), 7.3.

Follow `00-guardrails.md`. One step per session if you like; update `PROGRESS.md` after each.

## Goal

Capture what the approved app does today as automated tests, set up CI, inspect a **local copy** of the database, and draft the website API description, so every later change is checked against today's behaviour.

## Step 1 — Test infrastructure

1. Add the test projects listed in `TESTING.md` (start with `InpremClockingApp.Tests.Baseline` and `InpremClockingApp.Tests.Ui`; create the platform test projects empty) and the shared test-data builder described there.
2. Use `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`). If `Program` is not accessible to tests, add `InternalsVisibleTo` via the **test project** or a new file `InpremClockingApp/Properties/AssemblyInfo.Tests.cs` — ask before adding anything inside `InpremClockingApp`.
3. Database for tests: SQL Server in a container via `Testcontainers.MsSql` (Docker). If Docker is unavailable on the developer's machine, fall back to LocalDB on Windows; make the choice configurable.
4. Test data: a seeding helper that creates **two organizations** (Tenant A "Alpha", time zone `America/New_York`; Tenant B "Beta", time zone `Europe/London`), each with one admin, three staff, three volunteers and clockings across several days including a daylight-saving change day. No real personal data.
5. Authentication in tests: sign in through the real login page (form post) so the existing login code path is exercised.

## Step 2 — Behaviour baseline suite

Record current behaviour against commit `1061ea8`. Tests must pass on the unchanged code.

- **Kiosk flows** (`/StaffAttendance`, `/VolunteerAttendance`, `/staff-clockin/{id}`, `/volunteer-clockin/{id}` and `api/controls/...` endpoints): search, register, clock in, break start, break end, clock out; one session per person per day; working hours = (out − in) − (break end − break start).
- **Back office**: page loads and key content for `/BackOffice`, `/Staff`, `/StaffDetails`, `/Volunteer`, `/VolunteerDetails`, `/User`, `/StaffClocking`, `/VolunteerClocking`, `/StaffReport`, `/StaffClockingReport`, `/VolunteerReport`, `/VolunteerClockingReport`, `/HoursWorked`; create/edit/delete for staff, volunteers and clockings.
- **Reports and PDFs**: totals for fixed date ranges; PDF responses are valid PDFs and their extracted text contains the expected names, totals and organization header (use a PDF text extractor such as PdfPig).
- **Authorization as today**: anonymous users are redirected to login; `AdminOnly` pages require the Admin role.
- **Tenant isolation as today**: Tenant A admin cannot see Tenant B data on any of the above pages (record what happens today; if something leaks, **do not fix it** — report it).
- Kiosk JavaScript-driven behaviour: if HTTP-level tests cannot cover it, add a small Playwright (.NET) suite in `tests/InpremClockingApp.Tests.Ui`, run against the test server.

Name tests by flow (`Kiosk_Staff_ClockIn_Break_ClockOut_ComputesHours`, ...). Keep expected values in the test, not computed by the code under test.

## Step 3 — CI pipeline

Add `.github/workflows/ci.yml`:
- Triggers: pull requests and pushes to `mt/**` branches.
- Steps: restore, build (treat new warnings in new projects as errors), run all test projects with a SQL Server service container, publish test results and coverage (coverlet).
- Secret scanning (for example `gitleaks` action) and `dotnet list package --vulnerable --include-transitive` failing on high or critical.
- No deployment steps.

## Step 4 — Database inspection scripts (read-only)

Create `scripts/inspection/` with read-only T-SQL scripts and a `README.md` explaining how to run them **against a restored local copy only**:

1. `01-environment.sql`: `@@VERSION`, database name, compatibility level, recovery model, size, `IS_ROLEMEMBER('db_owner')`, `HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER ANY SECURITY POLICY')`.
2. `02-migration-history.sql`: list `__EFMigrationsHistory`; compare with the migration files on the branch (expected last: `20261003091001_AddVolunteerContactPersonDetails`).
3. `03-row-counts.sql`: row counts for every table.
4. `04-data-quality.sql`:
   - duplicate `AspNetUsers.NormalizedEmail` across all organizations;
   - `AspNetUsers` with null `TenantId` that are not in the `SuperAdmin` role;
   - clockings whose person belongs to another organization: `Clockings c JOIN Volunteers v ON v.VolunteerId = c.VoluntId WHERE c.TenantId <> v.TenantId`, and `ClockingsStaff cs JOIN Staffs s ON s.StaffId = cs.StafId WHERE cs.TenantId <> s.TenantId`;
   - duplicate clockings per person per `ClockDate` (should be zero);
   - row counts of `Subscriptions`, `Invoices`, `Tenants`.
5. `05-report-baseline.sql`: totals for three agreed reports (to compare before and after every migration).

Also document how to check model drift: `dotnet ef migrations has-pending-model-changes` (EF Core 8) run from `InpremClockingApp/`.

## Step 5 — Draft website API description

Create `docs/multitenancy/api/public-v1.draft.yaml` (OpenAPI 3) from design section 7.3: all 18 endpoints, request/response schemas, auth schemes (`signupToken`, `billingAccessToken` as bearer; optional `X-Api-Key`), `Idempotency-Key` header, `ProblemDetails` errors, checkout-session statuses. Base URL `https://app.inpremclockin.org/api/public/v1`. Mark it **DRAFT — may change** in `info.description`. This is documentation only; no code.

## Exit checks

| ID | Criterion |
|---|---|
| G0-4 | Baseline suite passes 100% on commit `1061ea8`, locally and in CI |
| G0-2 | Inspection scripts run on a local restored copy; results saved (without personal data) in `docs/multitenancy/inspection-results.md`; any duplicate admin emails or cross-tenant clockings listed |
| — | CI runs on every PR: build, tests, coverage, secret scan, vulnerable-package check |
| — | Draft OpenAPI file validates (e.g. with `spectral` or `swagger-cli validate`) |
| — | `git diff 1061ea8 -- InpremClockingApp/` is empty (no application file changed) |

## Report

Use the report format in the guardrails. List any isolation leaks or data-quality problems found in `PROGRESS.md` under follow-ups; Phase 0 fixes them.
