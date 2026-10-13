# Progress log

Claude Code updates this file at the end of every step. Newest entries at the top of the log.

## Current position

- **Phase:** 0 Foundation (in progress)
- **Next step:** Phase 0 / Step 0.3 — Tenant context and memberships
- **Branch:** `mt/phase0` (from `mt/integration` `1b59b6b`)

## Phase status

| Phase | Status | Exit checks passed | Merged into `mt/integration` |
|---|---|---|---|
| G0 Preparation | Done | G0-2 (on test data; see follow-ups), G0-4, CI, OpenAPI valid, no existing app file changed | 2026-10-13 (`ee940f8`) |
| 0 Foundation | In progress (Steps 0.1–0.2 done) | — | — |
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
- **Question for Phase 0 (design vs data).** Migration M2 marks "the earliest admin of each organization" as Owner. `AspNetUsers` has no creation date and its Id is a GUID, so "earliest" cannot be derived. The project owner names each organization's Owner (04 result 6 lists the admins).
- **Report baseline R1–R3 are proposed, not agreed.** `05-report-baseline.sql` uses staff hours per organization per month, volunteer hours per organization per month, and dashboard figures. Change them there if the project owner wants others.
- **Website API draft: open questions** (listed in the YAML's `info.description`):
  - Section 7.3 has 17 endpoints; the G0 prompt says 18. The 7.6 diagram's `POST /account/checkout-sessions` conflicts with the table. The draft follows the table.
  - The CAPTCHA field and consent flags are not named in the design (the draft uses `captchaToken`, `acceptTerms`, `acceptPrivacyPolicy`, `smsOptIn`).
  - Plan limits, invoice statuses and the staging hostname are placeholders.
- **Test project naming.** The Phase 0 file says to create `Inprem.Platform.Tests`; `TESTING.md` lists `.Unit`, `.Integration` and `.Architecture`, which G0 created. The three existing projects are used.
- **EF Core version.** The platform projects use EF Core **8.0.0**, the approved app's version, so referencing them changes nothing in the app's runtime. Upgrading EF Core (which fixes the known advisories) is still a deliberate step to take with a baseline run.
- **Deploying step 0.2.** Apply `scripts/migrations/platform-M1a-up.idempotent.sql` **before** deploying, because the app keeps its cookie keys in `platform.DataProtectionKeys`. Users are signed out once: the cookie is renamed to `.Inprem.App` and keys move from `App_Data` to the database. `Site:AppHost`/`AdminHost` are empty (any host) until the cutover. See `scripts/migrations/README.md`.
- **Tests tagged `ChangedBy`** record behaviour the design changes on purpose: same admin email in two organizations (D2/C3-6) and no lockout (C3-7). Both change in Phase 0 step 0.4.
- **Playwright browsers** are a one-time install per machine: `pwsh tests/InpremClockingApp.Tests.Ui/bin/Debug/net8.0/playwright.ps1 install chromium` (CI must do the same).

## Manual / sandbox test records

_(date — what was tested — result)_

## Log

_(date — phase/step — summary — tests — next)_

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
