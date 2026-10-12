# Progress log

Claude Code updates this file at the end of every step. Newest entries at the top of the log.

## Current position

- **Phase:** G0 (in progress)
- **Next step:** G0 / Step 4 — Database inspection scripts (CI still needs a first run on GitHub: push `mt/g0`)
- **Branch:** `mt/g0`

## Phase status

| Phase | Status | Exit checks passed | Merged into `mt/integration` |
|---|---|---|---|
| G0 Preparation | In progress (Steps 1–3 done) | — | — |
| 0 Foundation | Not started | — | — |
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
- **Model drift check:** the installed `dotnet-ef` global tool is 10.0.8 and does not recognise `migrations has-pending-model-changes`. Phase 0 needs a working drift check: install a matching `dotnet-ef` 8.x as a local tool, or add a test that compares the model to the migrations snapshot.
- **Test database modes:** `localdb` (Windows default) and `container` (`INPREM_TEST_DB=container`, Docker Desktop with Linux containers) both pass the Step 1 suite (container mode verified 2026-10-12; image `mcr.microsoft.com/mssql/server:2022-latest`; Testcontainers removes the container after the run). `external` mode is for CI's service container (Step 3).
- **Test-host differences from a normal run** (all in `InpremAppFactory`): environment `Testing` (no user-secrets or per-environment appsettings), `SeedAdmin`/`SeedSuperAdmin` blanked, in-memory data-protection keys, `BillingBackgroundService` not started.
- **House organization in every new database:** migration `MultiTenancy_Phase1a_BackfillTenantData` inserts "Inprem Holistic Community Resource Center" as tenant 1. The seed catalog exposes it as `House`; Alpha, Beta and Gamma are tenants 2–4.
- **Baseline findings F-01 to F-09** (`docs/multitenancy/baseline-findings.md`): recorded, not fixed. Each one has a passing test tagged `Finding`.
  - **F-08 (cross-tenant delete through the Move page handlers) is a security issue.** Phase 0 step 0.5's save interceptor should block it, because the posted entity's TenantId is never the caller's. When that lands, the F-08 test must flip to "Beta unchanged".
  - **The developer decides when to fix F-01 to F-07 and F-09.** Each fix is a deliberate behaviour change and updates its test with a reason.
- **Known secret in the approved app.** `InpremClockingApp/Services/EmailService.cs` has a hardcoded SMTP credential (since commit `10e1e22`). It is listed in `.gitleaksignore` so CI can block new secrets. Before P0-5 it must be rotated, moved to configuration and the ignore line removed. Ownership must be confirmed with the original author before rotating. Gitleaks' default rules miss it; the custom rule `csharp-network-credential` in `.gitleaks.toml` finds it.
- **Vulnerable packages in the approved app.** 9 high/critical transitive advisories, from the 8.0.0 EF Core / Identity packages and from `Microsoft.VisualStudio.Web.CodeGeneration.Design`. They are listed in `scripts/ci/known-vulnerable-packages.txt`, so the CI gate only fails on new ones. Upgrade them in Phase 0 (the app's csproj is not on the C2 list, so the developer needs to agree the change). P0-7 requires 0.
- **CI has not run on GitHub yet.** The workflow passed `actionlint`, and its build-and-test job passed in a local Linux mirror (SDK 8 container + SQL Server container: Baseline 303/303, UI 5/5, coverage produced). G0-4's "in CI" half needs `mt/g0` pushed.
- **Tests tagged `ChangedBy`** record behaviour the design changes on purpose: same admin email in two organizations (D2/C3-6) and no lockout (C3-7). Both change in Phase 0 step 0.4.
- **Playwright browsers** are a one-time install per machine: `pwsh tests/InpremClockingApp.Tests.Ui/bin/Debug/net8.0/playwright.ps1 install chromium` (CI must do the same).

## Manual / sandbox test records

_(date — what was tested — result)_

## Log

_(date — phase/step — summary — tests — next)_

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
