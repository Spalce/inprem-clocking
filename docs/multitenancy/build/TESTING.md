# Testing strategy

"Well tested" for this project means: the approved app provably behaves as before, no organization can reach another's data, every new feature has automated tests at the right level, and the whole suite runs on every push.

## Projects and tools

| Project | Type | Tools |
|---|---|---|
| `tests/InpremClockingApp.Tests.Baseline` | Behaviour baseline of the approved app (HTTP level) | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, AngleSharp (HTML), PdfPig (PDF text) |
| `tests/InpremClockingApp.Tests.Ui` | Browser tests for JavaScript-driven kiosk and forms | Playwright for .NET |
| `tests/Inprem.Platform.Tests.Unit` | Domain and application logic | xUnit, FluentAssertions, NSubstitute |
| `tests/Inprem.Platform.Tests.Integration` | Web + database: isolation, authorization, host, API, billing, notifications | xUnit, `WebApplicationFactory`, Testcontainers.MsSql (or LocalDB), WireMock.Net for PayPal/Twilio fakes |
| `tests/Inprem.Platform.Tests.Architecture` | Dependency and convention rules | NetArchTest |

Use a real SQL Server for integration tests (not the EF in-memory provider). Each test class gets a fresh database or a transaction that is rolled back.

## Test data

A shared builder seeds at least two organizations: **Alpha** (`America/New_York`) and **Beta** (`Europe/London`), each with an owner, an admin, three staff, three volunteers and clockings across several days including a daylight-saving change day. Add **Gamma** with no data for signup tests. No real personal data.

## Required suites

| Suite | What it proves | From |
|---|---|---|
| Behaviour baseline | Kiosk flows, back-office CRUD, reports, PDF text and totals, login and access rules are identical to commit `1061ea8` | G0 |
| UI (Playwright) | Kiosk clock-in/break/out, registration, back-office forms in a real browser | G0 |
| Tenant isolation | For **every route** in the endpoint table: Alpha's user requests Beta's ids → 403/404, and Beta's rows are unchanged (row hash before/after). Includes pages, API controllers, PDF downloads, website API account endpoints | Phase 0 |
| Query-filter coverage | Every entity with `TenantId` has a query filter and is covered by the save interceptor (reflection) | Phase 0 |
| Authorization matrix | Every route × role (anonymous, kiosk, org admin, org owner, provider support, billing, owner) → expected allow/deny | Phase 0, grows each phase |
| Host isolation | `SiteMode=App` → every provider route 404; `SiteMode=Admin` → every app/API/webhook route 404; cookies host-only; Admin site runs no job server | Phase 0 |
| Architecture | Layer dependencies; no `DbContext` in new pages/endpoints | Phase 0 |
| Security | Lockout; no secrets in repo (gitleaks); no high/critical vulnerable packages; security headers; token expiry; rate limits (429); CAPTCHA enforced; CORS; webhook signatures; redirect allow-lists | Phase 0, grows |
| Bootstrap and recovery | Every refusal path; single-use hashed codes; sessions revoked; notifications | Phase 0 |
| Migrations | All migrations apply to an empty DB and to a copy of the baseline schema; down scripts work; no pending model changes | Phase 0 |
| UI pattern | Every list page contains no POST form except search/filter (render-and-inspect) | Phase 1 |
| Audit | Every provider write action produces exactly one audit entry | Phase 1 |
| Billing | Checkout/subscription state machines; activation exactly once across confirm/webhook/reconciliation; duplicate and out-of-order events; mismatches fail | Phase 2 |
| API contract | OpenAPI matches implementation; examples execute; idempotency; error codes | Phase 3 |
| Notifications | Consent and opt-out respected; quiet hours by time zone; limits; delivery status | Phase 5 |
| Sandbox end-to-end (manual, recorded in `PROGRESS.md`) | PayPal sandbox and Twilio test flows that cannot be faked fully | Phases 2–6 |

## Coverage targets

- New Domain and Application code: at least 85% line coverage, 75% branch coverage.
- New Infrastructure and web code: covered by integration tests for every endpoint and page handler.
- Existing approved code: covered by the behaviour baseline (no coverage target, but every flow listed in G0).

## Running

- `dotnet test` runs everything except Playwright and sandbox tests; `dotnet test --filter Category=Ui` runs Playwright.
- CI (`.github/workflows/ci.yml`) runs all automated suites on every push and pull request, with a SQL Server service container, coverage report, gitleaks and the vulnerable-package check. The build fails if any suite fails or coverage drops below target.

## Rules

- A failing behaviour-baseline test means approved behaviour changed: stop and report; do not update the expected value.
- Never skip, weaken or delete a test to get green. If a test is wrong, explain why and ask.
- Bugs found later get a regression test first, then the fix.
