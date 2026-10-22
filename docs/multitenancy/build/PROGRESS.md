# Progress log

Claude Code updates this file at the end of every step. Newest entries at the top of the log.

## Current position

- **Phase:** 6 Staff alerts: implementation complete (6.1–6.3) with the stand-in, Phase 6 bundle rehearsed and confirmed on the local database. Waiting: the project owner's approval of C3-8 and Twilio sender registration (P6-1 as the design words it), real texts on staging (P6-3). Phase 5's Twilio checks (P5-1, P5-4) still wait for credentials
- **Merged:** Phase 6 into `mt/integration` (fast-forward to `ddc391d`, local; not pushed), developer's request.
- **Domain cutover:** C.1 to C.3 prepared (settings templates, the optional old-address redirect, runbooks). The DNS changes and deployments are the developer's, following `docs/multitenancy/runbooks/domain-cutover.md`, when deploying after all phases
- **Merged:** the cutover preparation into `mt/integration` (fast-forward to `29aa38a`, local; not pushed), developer's request.
- **Testing first (developer, 2026-10-20):** "finish everything before hardening". Phase 7 waits until the developer has tested Phases 0 to 6 with the guides in `docs/multitenancy/testing/`. Step 7.1 (anti-forgery) also needs a decision first (see the log entry of 2026-10-20, "Test guides").
- **Next step:** the developer tests with `docs/multitenancy/testing/`; then Phase 7 (its entry needs Phases 0–6 exit checks, several of which wait for PayPal, Twilio and staging)
- **Branch:** `mt/integration` (everything so far is merged there; pushed to `fa7b6a5`, later commits local)

## Phase status

| Phase | Status | Exit checks passed | Merged into `mt/integration` |
|---|---|---|---|
| G0 Preparation | Done | G0-2 (on test data; see follow-ups), G0-4, CI, OpenAPI valid, no existing app file changed | 2026-10-13 (`ee940f8`) |
| 0 Foundation | Implementation complete (0.1–0.8, 0.10; 0.9 moved to Phase 7). Deployment-dependent exit checks (P0-6 Level 3, P0-9 real copy, P0-11, P0-12) wait for the test-site deployment (developer's decision: deploy after all phases) | — | — |
| 1 Provider Console | Implementation complete (1.1–1.7), Phase 1 bundle rehearsed. Waiting: developer walkthrough (P1-1), onboarding under 3 minutes on staging (P1-3) | P1-2, P1-4, P1-5, P1-6, P1-7 (automated); P1-1 automated part | 2026-10-15 (`87ab80d`, developer's request, before the P1-1 walkthrough) |
| 2 PayPal core | Implementation complete (2.1–2.9), Phase 2 bundle rehearsed. Waiting: PayPal sandbox run (P2-1, P2-4) | P2-2, P2-3, P2-5, P2-6, P2-7 (automated); P2-1 and P2-4 with the pretend PayPal | 2026-10-15 (`9ac52c3`, developer's request, before the sandbox run) |
| 3 Website API | Implementation complete (3.1–3.7), Phase 3 bundle rehearsed. Waiting: J1–J6 with the PayPal sandbox and the web team's site on staging (P3-1); load test on staging (P3-7) | P3-2, P3-3, P3-4, P3-5, P3-6, P3-8, P3-9 (automated); P3-1 with the demo site and the pretend PayPal | 2026-10-16 (`ceeb80c`, developer's request, before the staging checks) |
| 4 Invoices | Implementation complete (4.1–4.4), Phase 4 bundle rehearsed and applied locally. Waiting: invoices and reconciliation in the PayPal sandbox (P4-1, P4-3) | P4-2 (automated); P4-1 and P4-3 with the pretend PayPal | — |
| 5 Twilio | Implementation complete (5.1–5.3) with the stand-in, Phase 5 bundle rehearsed and applied locally. Waiting for Twilio test credentials: real texts, P5-1 (Verify), P5-4 on staging | P5-2, P5-3 (automated); P5-1 with the stand-in and a pretend Verify; P5-4 with simulated reports | 2026-10-20 (developer's request, before the Twilio checks) |
| 6 Staff alerts | Implementation complete (6.1–6.3) with the stand-in, Phase 6 bundle rehearsed and confirmed locally. Waiting: project owner approval of C3-8 and Twilio sender registration (design P6-1), real texts on staging (P6-3) | P6-1 setting part, P6-2, P6-3 (automated, with the stand-in) | 2026-10-20 (`ddc391d`, developer's request, before the project owner and Twilio checks) |
| Cutover | C.1–C.3 prepared (templates, `LegacyRedirect` off by default, runbooks, live check script). Waiting: DNS, certificates and deployments by the developer; the web team's site and redirect rules | Redirect rules: every App-site route 301 (automated); templates start their site (automated). On the live addresses: `check-cutover.ps1` after each step | 2026-10-20 (`29aa38a`, developer's request, before the DNS changes and deployments) |
| 7 Hardening | Not started | — | — |

## Open follow-ups and shortcuts

- **G0 exit check wording:** `git diff 1061ea8 -- InpremClockingApp/` is not empty; it contains one new file, `InpremClockingApp/Properties/AssemblyInfo.Tests.cs` (`InternalsVisibleTo` for the test projects, approved by the developer 2026-10-12). Read the check as "no existing application file changed".
- **Model drift check: done (2026-10-14).** The `dotnet-ef` local tool is now pinned to 8.0.32, which has `has-pending-model-changes`; no drift in either context. ~~**Model drift check:** works with `dotnet-ef` 8 and reports no drift on `1061ea8` (2026-10-13). `InpremClockingApp/.config/dotnet-tools.json` pins `dotnet-ef` **7.0.2**, which has no `has-pending-model-changes`; update the pin to 8.0.31 in Phase 0 (one-line change to an existing file, needs agreement). Until then, see `scripts/inspection/README.md`. `CLAUDE.md`'s note that the model has moved on from the migrations is out of date.~~
- **Test database modes:** `localdb` (Windows default) and `container` (`INPREM_TEST_DB=container`, Docker Desktop with Linux containers) both pass the Step 1 suite (container mode verified 2026-10-12; image `mcr.microsoft.com/mssql/server:2022-latest`; Testcontainers removes the container after the run). `external` mode is for CI's service container (Step 3).
- **Test-host differences from a normal run** (all in `InpremAppFactory`): environment `Testing` (no user-secrets or per-environment appsettings), `SeedAdmin`/`SeedSuperAdmin` blanked, in-memory data-protection keys, `BillingBackgroundService` not started.
- **House organization in every new database:** migration `MultiTenancy_Phase1a_BackfillTenantData` inserts "Inprem Holistic Community Resource Center" as tenant 1. The seed catalog exposes it as `House`; Alpha, Beta and Gamma are tenants 2–4.
- **Baseline findings F-01 to F-09** (`docs/multitenancy/baseline-findings.md`): recorded, not fixed. Each one has a passing test tagged `Finding`.
  - **F-08 (cross-tenant delete through the Move page handlers) is a security issue.** Phase 0 step 0.5's save interceptor should block it, because the posted entity's TenantId is never the caller's. When that lands, the F-08 test must flip to "Beta unchanged".
  - **The developer decides when to fix F-01 to F-07 and F-09.** Each fix is a deliberate behaviour change and updates its test with a reason.
- **Known secret in the approved app.** `InpremClockingApp/Services/EmailService.cs` has a hardcoded SMTP credential (since commit `10e1e22`). It is listed in `.gitleaksignore` so CI can block new secrets. Before P0-5 it must be rotated, moved to configuration and the ignore line removed. Ownership must be confirmed with the original author before rotating. Gitleaks' default rules miss it; the custom rule `csharp-network-credential` in `.gitleaks.toml` finds it.
- **Vulnerable packages in the approved app: fixed 2026-10-14.** All packages moved to their 8.0.x patches (see the log). ~~**Vulnerable packages in the approved app.** 9 high/critical transitive advisories, from the 8.0.0 EF Core / Identity packages and from `Microsoft.VisualStudio.Web.CodeGeneration.Design`. They are listed in `scripts/ci/known-vulnerable-packages.txt`, so the CI gate only fails on new ones. Upgrade them in Phase 0 (the app's csproj is not on the C2 list, so the developer needs to agree the change). P0-7 requires 0.~~
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
- **EF Core version: upgraded 2026-10-14** to 8.0.32 in the app and the platform projects together. ~~**EF Core version.** The platform projects use EF Core **8.0.0**, the approved app's version, so referencing them changes nothing in the app's runtime. Upgrading EF Core (which fixes the known advisories) is still a deliberate step to take with a baseline run.~~
- **Deploying step 0.2.** Apply `scripts/migrations/platform-M1a-up.idempotent.sql` **before** deploying, because the app keeps its cookie keys in `platform.DataProtectionKeys`. Users are signed out once: the cookie is renamed to `.Inprem.App` and keys move from `App_Data` to the database. `Site:AppHost`/`AdminHost` are empty (any host) until the cutover. See `scripts/migrations/README.md`.
- **No account is created at startup any more (step 0.6).** Existing databases keep their admins. A new, empty database has no account until the provider bootstrap of step 0.7. `ROLES.md` and `Test.md` were updated. P0-5 ("secret scan 0 findings") still needs the `EmailService` credential rotated and its `.gitleaksignore` line removed.
- **Provider console sessions: no 8-hour / 30-minute limits yet (step 0.7).** Design 5.5 asks for 8 hours maximum and 30 minutes idle on the Admin site, plus an IP allow-list. The step 0.7 text doesn't ask for them; they belong with the console in Phase 1. Lockout (5 tries / 15 minutes) already applies.
- **No QR code on the two-factor page (step 0.7).** The app ships no QR library, and none is loaded from outside. The page shows the key and an `otpauth://` link, which every authenticator app accepts by typing. A QR code can come with the Phase 1 console.
- **One Provider Owner until Phase 1 (D16).** Inviting provider users is a Phase 1 console screen. Until then the console banner and `/health` (Degraded) warn, as designed. Level 2 (peer reset) is a tested use case without a screen until Phase 1.
- **Level 4 completes by command after its delay (step 0.7).** `provider recover --complete --id <id>` is run by the operator once the 24 hours have passed. The background jobs of step 0.8 could do it automatically; that is an open choice.
- **"Forgot password" can't send email locally: fixed 2026-10-14** (`EmailService` honours `Email:DropFolder` in Development). ~~"Forgot password" can't send email locally (found 2026-10-14).~~ The approved page calls `EmailService` directly (C1), so `Email:DropFolder` does not cover it. To fix it with the `EmailService` credential follow-up: move SMTP settings to configuration, with the same local folder option.
- **Provider emails go through the approved app's mailbox (step 0.7).** Bootstrap and recovery emails use `IEmailSender` → `ApprovedAppEmailSender` → `EmailService`, the hardcoded SMTP account still waiting on the owner's confirmation. "SMS where configured" (design 5.4) comes with Twilio in Phase 5.
- **Bootstrap after an unused, expired first invitation (step 0.7).** A new bootstrap code invites the same person again; the used code is refused for good (runbook section 2).
- **The old billing sweep no longer runs (step 0.8).** Removing `BillingBackgroundService` (C2-2, as the step asks) means no automatic renewal, past-due or suspension of organizations until Phase 2. `BillingService` and the Platform pages (C4) are unchanged.
- **Jobs are off by default (step 0.8).** `Jobs:RunServer` is `false` in `appsettings.json`; the App site's deployment sets it to `true`. There are no jobs yet.
- **Hangfire install script follows the package (step 0.8).** `scripts/migrations/hangfire-up.idempotent.sql` comes from Hangfire.SqlServer 1.8.14's installer. Upgrading the package means regenerating and reviewing it.
- **Rehearsed on synthetic data only (step 0.10).** The Phase 0 bundle was rehearsed with rollback (R2 and R3) on a disposable database with synthetic data, at about the size of the real one. Before a shared or production database, it must be repeated on a restored copy of that database (`rehearse-phase0.ps1 -Existing`). The CTO then signs it (design 11.2–11.4); see `docs/multitenancy/rehearsal-phase0.md`.
- **M1 is M1a + M1b (step 0.10).** The step's "empty platform tables" already exist from step 0.7: provider users, invitations (named `ProviderInvitations`), audit and recovery records. No duplicate migration was added.
- **`appsettings.Production.json` on the server (step 0.10).** `prepare-site.ps1` never ships it (a local publish would include the developer's copy). Upload with Web Deploy *without* "remove additional files", or move the connection string into the host's settings.
- **PayPal sandbox smoke test of steps 2.2 to 2.8 not run yet.** Locally it also needs, besides the keys: `PayPal:ReturnUrlAllowList:0` = the app's https address (e.g. `https://localhost:7xxx`), `PayPal:PlanReturnUrl` = that address + `/Billing/PayPalReturn`, `PayPal:PlanCancelUrl` = that address + `/Billing`. It needs the sandbox keys (`PayPal:ClientId`, `PayPal:Secret`) in user-secrets; the client is tested against a pretend PayPal only.
- **Low advisory GHSA-g4vj-cjjj-v7hg (NuGet.Packaging / NuGet.Protocol 6.11.0)** is reported in the app and test projects. It was not introduced by step 2.2 (it comes with the existing design-time packages). Low severity, so the CI gate does not fail; it is reviewed with the other packages in Phase 7.
- **PayPal sandbox access from Ghana (2026-10-15).** The developer is in Ghana, where PayPal may only allow send-only personal accounts; it is unclear whether such an account can open developer.paypal.com. If not, the project owner creates the sandbox app on Inprem's PayPal Business account and shares the two sandbox keys privately. ngrok is not affected.
- **"Keep" cannot withdraw a cancellation (step 3.5).** Cancelling cancels the PayPal subscription at once, and PayPal cannot reactivate a cancelled subscription. The organization keeps access to the period end either way. So `POST /account/subscription/keep` always answers 409 `cannot_keep` and points to a checkout with purpose `resubscribe`. An "undo" would need cancelling to be scheduled for the period end instead; decide with the web team whether that is wanted.
- **`GET /account/invoices` is empty until Phase 4** (PayPal Invoicing). Payments are on the in-app Billing page meanwhile.
- **Billing links need `Links:WebsiteBaseUrl` (step 3.5).** Without it the job sends nothing (and the API still answers 202). Set it to the website's address (e.g. `https://inpremclockin.org`); the website's billing page then receives `?code=…`.
- **Website return addresses must be on the PayPal allow-list (step 3.4).** The website's return and cancel URLs are checked against `PayPal:ReturnUrlAllowList` (refused with `return_url_not_allowed`). When the website goes live, add its origins there too, next to the app's (`https://inpremclockin.org`, `https://www.inpremclockin.org`, staging).
- **CAPTCHA needs its secret (step 3.3).** Registration refuses every request until `Captcha:Secret` is set (fail closed). For local testing, Cloudflare publishes test keys: the secret `1x0000000000000000000000000000000AA` always passes and goes in user-secrets (`dotnet user-secrets set "Captcha:Secret" "<test secret>" --project InpremClockingApp`). The real secret comes from whoever runs the website's Cloudflare account (needs list, item 8).
- **Owner email already has an account (step 3.3).** The draft's `email_in_use` (409) is used, as agreed. It tells a caller that an email has an account; CAPTCHA and the write rate limit make that slow to abuse. Revisit with the web team if they prefer an invitation instead (the console already invites existing accounts).
- **Signup status "Active" (step 3.3).** `GET /signups/{id}` answers `Active` once the email is verified and the subscription is Trialing or Active, `Verified` when verified but not yet paid (subscribe mode), else `PendingVerification`.
- **Draft API changes proposed (steps 3.1, 3.2).** In `docs/multitenancy/api/public-v1.draft.yaml`: `internal_error` added to the problem codes (the draft had no code for a 500); `limits.maxPeople` added to plans (staff and volunteers together, what plans have today; the draft's separate staff, volunteer and admin limits wait for the plan catalogue decision, PO-5). Confirm with the web team when the integration guide is handed over (step 3.7).
- **Old billing tables still mapped (step 2.9).** `Models/Billing` (Subscription, Invoice and their enums) stays as table mappings in `ApplicationDbContext`, marked legacy, so the app's schema does not change before M6 (Phase 7) drops the tables. Nothing reads or writes them any more. This is a deviation from the step text's "remove Models/Billing": removing the mappings would make the next app migration drop the tables early.
- **Old billing rows to review (step 2.9).** M5 moves paying Active/PastDue old subscriptions to Invoiced; others are left and counted (`LeftForReview`). Unpaid or void old invoices stay for Phase 4. Locally: both paying old subscriptions moved, 1 paid invoice became a payment, 1 void invoice left.
- **People limit not enforced yet (step 2.7).** `AccessStatus.CanAddPeople` knows the plan's limit (or the console override), but refusing a new staff member or volunteer means touching the approved add-person flows (a C3 change). Decide with the developer whether, and how, before plans with limits are sold.
- **Kiosk buttons on a page already open (step 2.7).** If an organization becomes read-only while a kiosk page is open, its buttons get the 403 and the approved page shows its usual "Oops! An error occured" toast (the approved page is not changed). Opening the kiosk again shows the friendly "kiosk is paused" page.
- **Owner emails for payment problems wait for Phase 5 (step 2.5).** Webhooks change the subscription (past due, suspended, cancelled, expired) and audit it, but only activation, plan change and the owner's own cancel email the owners so far. The payment-failed, suspended and similar messages are in the Phase 5 alert catalogue.
- **Billing alerts by email only (step 2.4).** A checkout that fails verification emails active provider Owners and Billing users ("[Inprem billing]"). SMS and the console notification list come in Phase 5.
- **P1-1 walkthrough still open.** Phase 1 was merged into `mt/integration` at the developer's request before the walkthrough. Record it here when done.
- **Existing organizations are on Free (step 2.1, D26).** M1f gives every organization without a subscription a Free one, so billing enforcement (step 2.7) changes nothing for them. Moving one to a paid plan is a console action (step 2.8). Decide per organization before go-live.
- **Antiforgery on the App site (found in step 1.5).** A POST without a token to an Account-area page got 400, even though `Program.cs` applies `IgnoreAntiforgeryToken` to all pages. Forms built with tag helpers carry the token, so nothing visible breaks. Check how the global convention really behaves before C3-10 (Phase 7).
- **Support sessions replace the console sign-in on a combined site (step 1.5).** With `SiteMode=All` (local), the hand-off sign-in replaces the provider's console session in the shared cookie; sign in to the console again afterwards. Separate App and Admin hosts (production) are not affected.
- **Support history page has no menu link yet (step 1.5).** `/Account/SupportSessions` exists for organization admins. Linking it from the approved sidebar is a C3 change, for the UI work in Phase 7, or say so earlier.
- **Tests tagged `ChangedBy`** record behaviour the design changes on purpose: same admin email in two organizations (D2/C3-6) and no lockout (C3-7). Both change in Phase 0 step 0.4.
- **Staff alerts need the project owner and Twilio (Phase 6 exit).** The design's P6-1 also asks for the project owner's approval of C3-8 and for Twilio's approval of the sender registration; Phase 6's entry asks for Twilio's answer on whether each organization needs its own registration, recorded here. None of these has happened. Until then, staff alerts stay off for every organization (the default), and texts go only to the stand-in. Record both answers here when they come.
- **Staff texts in quiet hours are dropped (step 6.3, developer's decision).** With the D32 default missed clock-out time (21:00, the start of quiet hours), no missed clock-out text is ever sent; the settings page says so. Ask the project owner whether the default should move before 21:00 (e.g. 20:30): that would change D32.
- **Playwright browsers** are a one-time install per machine: `pwsh tests/InpremClockingApp.Tests.Ui/bin/Debug/net8.0/playwright.ps1 install chromium` (CI must do the same).

## Manual / sandbox test records

- **2026-10-14 — Developer's local test, remaining checks: passed.**
  - **Provider sign-in:** password, then authenticator code, then the console (B8).
  - **Level 1:** a recovery code allowed only re-enrolment; a new key and new codes were issued (B9–B10).
  - **Organization admin:** sign-in showed "Choose organization" for the two organizations, each showing only its own data (A1–A3). The password was reset with a link generated locally, because "Forgot password" (approved page, `EmailService`) cannot reach the SMTP server from this machine.
  - **Not tested locally:** A4–A7, and Level 3 (C1, on the test site).

- **2026-10-14 — Developer's local test, provider bootstrap (checklist B1–B6): passed.**
  - First Provider Owner created through `/Provider/setup`.
  - Setup link opened, password set, Google Authenticator enrolled with the QR code, recovery codes shown, console reached. Database: owner Active with two-factor; audit trail complete.
  - Found and fixed on the way:
    - typing the key failed in Google Authenticator → the QR code was added;
    - the local SMTP server was unreachable and the half-done bootstrap blocked retries → bootstrap now undoes itself on email failure, and Development can save emails to a folder (`Email:DropFolder`);
    - new package advisories → patched crypto packages pinned.

- **2026-10-13 — Local database prepared for the Phase 0 manual test** (by Claude Code, at the developer's request).
  - `DB_A65635_inpremdb` on `localhost`: backed up (verified), then `phase0-up.sql` applied.
  - The first run stopped safely at the M3 guard (error 50003): the developer's own email had admin accounts in organizations 10 and 12. With the developer's approval, they were merged into one account, Owner of both organizations. The re-run completed, and all six verify scripts gave the expected values.
  - Clocking data and hour totals are unchanged; the only differences are the merged account and its role row.
  - user-secrets set: `Site:SiteMode = All`, `Jobs:RunServer = true`.
  - Checklist: `docs/multitenancy/manual-test-phase0.md` (results pending).

_(date — what was tested — result)_

## Log

- **2026-10-22 — Phase 7 step 7.6 (security review), parts a to c; branch `mt/phase7`.**
  - **Started early at the developer's request** ("yes, so long as it will not break anything built and tested"), before the Phase 0–6 exit checks that wait for PayPal, Twilio and staging. The plan was approved with the policy in report-only mode by default.
  - **a. Headers:**
    - `Hosting/SecurityHeaders.cs` (new) is registered in `Hosting/SiteHosting.cs`; `Program.cs` and the frozen files are unchanged.
    - It adds `nosniff`, `Referrer-Policy`, `X-Frame-Options` and `Permissions-Policy`, and a Content-Security-Policy set by `SecurityHeaders:ContentSecurityPolicy` (`ReportOnly` default, `Enforce`, `Off`; added to `appsettings.json`, C2-11).
    - Reports go to `/csp-report`, logged without query strings, at most 30 a minute.
    - Tests: `SecurityHeadersTests` (23); `ContentSecurityPolicyBrowserTests` (4): every owner, console and signed-out page in a browser with the policy enforced, nothing blocked.
    - **All browser tests now run with the policy enforced** (`KestrelAppFixture`).
  - **b. Reads past the organization filters:** 20 `IgnoreQueryFilters` calls in 6 web files, each reviewed, all OK. The architecture test `CrossOrganizationReadTests` pins them.
  - **c. Logs:** `Security/LogScrubbingTests` runs sign-up, verification, sign-in, a billing link, a PayPal webhook and an incoming text. It found no password, token, code, full email, phone number or payload in any log line. A probe checks that the search works.
  - **Docs:** `docs/multitenancy/security-review.md` (new); `runbooks/site-configuration.md` has the new setting.
  - **Found by the enforced browser tests:** Playwright's `WaitForFunctionAsync` evaluates a string, which the policy refuses (no `'unsafe-eval'`). The demo website test now waits with a locator; the app itself needed nothing.
  - **Tests:** Unit 262, Architecture 14, Baseline 314, Integration 352, UI 17 (959), all passing.
  - **Left in 7.6:** the OWASP ZAP baseline scan (needs Docker Desktop), the low NuGet advisory, and the `EmailService` credential (waits for the mailbox owner). Switching the policy to `Enforce` comes after clean reports on the test site.

- **2026-10-21 — Website API test guides; two API fixes found while writing them.**
  - **Guides:** `docs/multitenancy/testing/website-api.md` (PowerShell, TA-1 to TA-13) and `website-api-swagger.md` (the docs page, TS-1 to TS-10). Both say where `captchaToken` comes from. Every expected answer was checked against the local copy.
  - **Fix: the same `Idempotency-Key` with a changed body now answers `422 idempotency_key_reused`.**
    - Before, it returned the first answer: the endpoints bind their JSON body before the filter runs, so the filter hashed an empty stream.
    - `UsePublicApi` now buffers the body of endpoints marked `RequiresIdempotencyKey`, and `IdempotencyFilter` rewinds it before hashing.
    - The existing test endpoint read no body, which is why the tests missed it. New test: `SignupTests.The_Same_Key_With_A_Changed_Signup_Is_Refused_Not_Answered_From_The_First` (failed before the fix).
  - **Fix: the OpenAPI document declares the `Idempotency-Key` header and each endpoint's token** (`PublicApiOperationFilter`), as the agreed draft does, so the docs page can send them. The contract test `OpenApi_Is_Published_And_Matches_The_Agreed_Draft` now also compares tokens and the header with the draft.
  - **Left for later:** the generated `ProblemDetails` lacks `code`, `traceId` and `errors`; `trialEndsAt` and `currentPeriodEnd` are written without `Z`.
  - **Tests:** Unit 262, Architecture 11, Baseline 314, Integration 328, UI 13 (928), all passing.
  - **Test data:** "API Test Centre 27199" was created in the local database while checking the guide.

- **2026-10-20 — Handover document (developer: the Claude subscription ends in about two days).** `docs/multitenancy/HANDOVER.md` covers:
  - what to ask the project owner for;
  - where settings go;
  - PayPal and Twilio dashboard set-up and settings (no code or table changes needed);
  - tests, migrations, the deployment plan and going live, including clearing sandbox plan ids;
  - the Phase 7 work with its files;
  - errors and their fixes, and open decisions.

- **2026-10-20 — Audit tab shows who by email; two old test-data scripts removed (developer: "merge, delete the two scripts, and fix the audit tab").**
  - **Merged:** `mt/test-guides` into `mt/integration` (fast-forward to `4ec8ff6`, local; not pushed). This fix on `mt/audit-tab-fix`.
  - **Audit tab (the developer's T1-6 note):** an organization's **Audit** tab in the console showed the raw actor (`provider:<id>`); the **Audit log** page and the dashboard show the email. The tab now uses the same lookup (`AuditLogService.ActorNamesAsync`, loaded only when the tab is open). Files: `Areas/Provider/Pages/Tenants/Details.cshtml(.cs)` (new code from Phase 1, not frozen). Actors without an account (jobs, the website) still show as recorded.
  - **Test first:** Integration `OrganizationPagesTests.Organization_Audit_Tab_Shows_Who_By_Email_Like_The_Audit_Log`: failed on the old line (checked by putting it back), passes now.
  - **Removed:** `scripts/reset-test-data.sql` and `scripts/seed-test-data.sql` (see the entry before). Nothing else used them.
  - **Test guides updated:** the known issue is gone from `docs/multitenancy/testing/README.md`, and T1-6 expects the email.
  - **Results (full run, one suite at a time):** Unit 262, Architecture 11, **Baseline 314**, Integration 327, UI 13 — all passed.
- **2026-10-20 — Test guides for Phases 0 to 6 and the cutover; old guides removed (developer: "finish everything before hardening. Give me runbooks to test what has been implemented so far").**
  - **Phase 7 paused before any code.** Step 7.1 was looked at first and stopped at a question, which stays open:
    - The plan adds the anti-forgery token through a script line in `_MainLayout` and "the kiosk layout", for jQuery calls. In the code, the back-office pages use `_Layout` (C1, frozen) and send their changes with `fetch`, not jQuery (Move, clocking Edit and actions, the staff and volunteer editors).
    - `Pages/StaffClocking.cshtml.cs` (C1) opts out of anti-forgery itself.
    - The kiosk's clock actions are GET requests to `ControlsController`, which anti-forgery never checks.
    - Options put to the developer: inject the script without editing frozen files; a design revision; or an Origin-header check instead of tokens. Decide before 7.1.
  - **New:** `docs/multitenancy/testing/`:
    - `README.md`: set-up of the local copy (what the user-secrets give, where emails and texts land), how to get around, known issues, not testable yet, results sheet;
    - one guide per phase: `phase0-everyday-clocking.md` … `phase6-staff-alerts.md`, and `cutover-checks.md`.
    - Every test has a use case, the exact clicks (labels read from the pages), what each click does, and the expected result.
    - IDs T0-1 to T4-10 are kept from the earlier test guide (a Claude Doc, "test guide (Phases 0 to 4)", where the developer's results are). New: T0-9, T1-11, T5-1 to T5-10, T6-1 to T6-9, TC-1 to TC-4.
  - **Known issues found while writing them (not fixed):**
    - An organization's **Audit** tab in the console shows the raw actor id (`provider:…`), while the **Audit log** page shows emails (the developer's T1-6 note).
    - **Extend trial** only shows for a trial without PayPal, so not for existing organizations, which are on Free (T2-2 note).
    - The demo website always sends `smsOptIn: false`, so owner texts can't be tried by hand.
  - **Removed (superseded by the new guides):** root `Test.md` (the rejected multi-tenancy branch's guide), root `TESTING.md` (the pre-multi-tenancy manual test guide), `docs/multitenancy/manual-test-phase0.md`.
  - **Kept on purpose:** `ROLES.md`, `UI-modification.md` and `backoffice-review.md` are cited by comments in frozen (C1) code files; `kiosk-review.md` is `backoffice-review.md`'s companion; `DEMO-SCRIPT.md` is needed by step 7.7; the rehearsal, inspection and baseline-findings records are referenced by PROGRESS and the tests.
  - **Flagged:** `scripts/reset-test-data.sql` and `scripts/seed-test-data.sql` (used only by the removed root `TESTING.md`) predate organizations. The reset deletes every organization's people and every account but one, Provider Console accounts included. Removed the same day at the developer's request (next entry).

- **2026-10-20 — Domain cutover C.1 to C.3 (developer: "yes, prepare the cutover").**
  - **Branch** `mt/domain-cutover` from `mt/integration` (after the Phase 6 merge). Code, settings and runbooks only; no DNS change and no deployment (the developer's, after all phases).
  - **Found while preparing:** with `Site:AppHost` set, the App site answers 404 on every other address. While `inpremclockin.org` still serves the app (the plan's interim), `Site:AppHost` must therefore stay **empty** on the App site; it is set only after the root has moved to the website (runbook step 6). The templates and the runbook say so. No code change for it.
  - **C.1 Settings:** templates without secrets in `scripts/deploy/templates/`:
    - `app-site.production.json`, `admin-site.production.json`;
    - `test-site.single-host.json` (SiteMode All, D9: the SmarterASP test site today; its address is a placeholder).
    - `docs/multitenancy/runbooks/site-configuration.md` lists every setting per site, the secrets that stay on the host, the staging rule (swap every address), and the PayPal and Twilio webhook addresses.
    - `SiteTemplateTests`: each template starts the app as its site (all startup checks pass) and holds no secret-shaped key or address; the two production templates agree on their addresses; jobs on the App site only; PayPal Sandbox.
  - **C.2 Old addresses:**
    - `Hosting/LegacyRedirect.cs` (new): `LegacyRedirect:Enabled` (off), `FromHosts`, `TargetBaseUrl`, checked at startup (no https target, no hosts, or a target that is one of the old hosts are refused).
    - On an old host, the first path segment is matched against 14 rules (`/Account`, `/api`, `/BackOffice`, `/Billing`, `/Error`, `/HoursWorked`, `/Identity`, `/Index`, `/Privacy`, `/Settings`, `/Staff*`, `/User`, `/Volunteer*`, `/webhooks`). A match gets 301 to the same path and query on `app.`; `/` and other paths are left for the website.
    - It runs first in the pipeline (a startup filter, so an `http://` link takes one hop). It is registered in `SiteHosting.cs` (Phase 0 file), so **no change to `Program.cs`**. `appsettings.json` (C2-11) has the section, off.
    - `docs/multitenancy/runbooks/root-redirects.md`: the same rules for the web team (generic, nginx, Apache, IIS), website page names to avoid (e.g. `/volunteer` matches `/Volunteer*`), webhooks and forms after a 301, a check.
    - `LegacyRedirectTests` (17): every App-site route from the endpoint table on the old host → 301 with its query; old links on `inpremclockin.org` and `www.`; GET and POST; other paths, the new host and the setting off unchanged; wrong settings refused; the runbook lists exactly the code's rules. The first run found one uncovered route: the tests' own stand-in controller (`/app-probe`), now left out of the check.
  - **C.3 Runbook** `docs/multitenancy/runbooks/domain-cutover.md`, each step with its undo:
    1. before you start (one site or two);
    2. DNS and certificates;
    3. deploy both sites (App bound to `app.` and still to the root);
    4. PayPal and Twilio webhooks to `app.`;
    5. kiosk devices, the message to the organization, the optional `LegacyRedirect`;
    6. root to the website with the web team's rules;
    7. tidy up (`Site:AppHost`).
    - A table of what each failing check usually means. Menu names checked against the pages (Dashboard, Staff Clocking, Volunteer Clocking; console Notifications → Send me a test message, Delivery log).
  - **Live check script** `scripts/deploy/check-cutover.ps1 -Stage Interim|Redirect|Website`: GET only, no redirects followed, PASS/FAIL per check, exit 1 on a failure. `CutoverCheckTests` (7) prove that the answers it expects are the app's (App and Admin site with production hosts). Run once against the real addresses (2026-10-20): `app.` and `admin.` "no answer" (they do not exist yet), the root still serves the app (PASS), as expected before step 1.
  - **Acceptance (09-domain-cutover.md):** "Redirects: every enumerated old path 301" passes (automated). "App on new host", "Admin isolated" against the live addresses and "Integrations" wait for the deployment: the in-process suites cannot run against a URL, so `check-cutover.ps1` is the check on the live sites, with the host-isolation suite in-process.
  - **Results (full run, one suite at a time):** Unit 262, Architecture 11, **Baseline 314**, Integration 326 (31 new), UI 13 — all passed.
- **2026-10-20 — Phase 6 bundle and exit review (developer: "Phase 6 bundle + exit review").**
  - **Bundles:** `scripts/migrations/phase6-up.sql` and `phase6-down.sql`, generated by `build-phase6-bundle.ps1` from M1p, with the earlier guards (refusing a database without the Phase 5 schema: 50080; unknown migrations: 50081, 50082). CI checks they are up to date. The earlier bundles rebuild unchanged.
  - **Rehearsal (disposable database on the local SQL Server, synthetic data, dropped afterwards):**
    - without Phase 5 → refused (50080);
    - after Phase 5 and one existing settings row: up, up again, down, down again, up; verify after up `1|4|0|0`; the existing row got `0|21:00|60|0`;
    - an unknown platform migration → refused (50082);
    - app data unchanged (fingerprint).
  - **Local database:** verified backup `DB_A65635_inpremdb_before_phase6_bundle_20261020.bak` first; the bundle found M1p applied and reported complete; verify `1|4|0|0`; app data unchanged. (The first attempt stopped before the bundle, after a verified backup: the script's own check could not read SQL Server's backup folder. The check was removed; `RESTORE VERIFYONLY` is the proof.)
  - **Exit checks:**
    - **P6-1 Setting: passes for the setting.** The fields and the alerts appear only when the organization switches staff alerts on (`StaffConsentTests`, `StaffAlertsFlowTests`). **Waiting:** the design's P6-1 also needs the project owner's approval of C3-8 and Twilio's approval of the sender registration.
    - **P6-2 Behaviour: passes.** With alerts off, the approved forms are unchanged: Baseline 314.
    - **P6-3 Alerts: passes with the stand-in.** The missed clock-out text comes at the set local time in New York and London, also on clock-change days (unit and integration tests). **Waiting:** real texts on staging (Twilio and the test site).
  - **Merged into `mt/integration`** (fast-forward to `ddc391d`, local; not pushed), at the developer's request, before the project owner and Twilio checks.

- **2026-10-20 — Phase 6 / Step 6.3: staff alerts (developer: "go ahead with 6.3").**
  - **Two decisions by the developer (2026-10-20):**
    - **Quiet hours:** a staff text that would fall in quiet hours (21:00 to 08:00, the organization's time) is **dropped, not held** for the morning: "still clocked in" is no longer true by then. It is recorded as Skipped with reason `quiet-hours`. With the D32 default (21:00) the missed clock-out text is therefore never sent; the settings page now says so and suggests a time before quiet hours (e.g. 20:30). D32 itself is unchanged.
    - **End-of-day summary: email only.** The admins' text consent was given for account messages, not shift summaries.
  - **Check (`Application/Notifications/StaffAlerts.cs`, `StaffAlertsCheck`):** for each organization with staff alerts on, in its own time zone (organizations grouped by zone):
    - missed clock-out: from the set time of the local day, a text to everyone who clocked in before it and is still clocked in;
    - long break: a text once the open break is longer than the set minutes;
    - summary (when chosen): from the set time, one email to the owners and admins listing who is still clocked in; nothing when nobody is.
  - Texts go through the Phase 5 module (consent, STOP, limits) to the person only (`RecipientType.Person`, `staff:{id}` / `volunteer:{id}`). Each alert has an event key for the person and the local day, so nothing is sent twice. One organization's failure does not stop the others.
  - **Notification module:** `NotificationEvent.DropInQuietHours` (default false, so the existing alerts are unchanged) and `SkipReasons.QuietHours`. **New messages:** `staff.missed-clock-out`, `staff.long-break` (texts), `staff.shift-summary` (email), editable in the console like the others.
  - **Reading (`InpremClockingApp/StaffAlerts/StaffAlertSource.cs`, new):** the active organizations with alerts on and their open sessions of the local day. A background job has no signed-in organization, so the query filters are bypassed on purpose and every query names the organization; read-only.
  - **Job (`StaffAlertsJob`, new):** every 5 minutes, UTC. The plan's "job per time zone bucket" is done as one job that groups organizations by zone and works out each one's local time itself, so clock changes need nothing special; an organization's set time is met within 5 minutes. Registered with the other recurring jobs (`JobsHosting.cs`, a Phase 0 file, one line).
  - **Settings page:** a line about quiet hours; the summary box says "email". No approved page changed. No model change, so no migration.
  - **Tests:**
    - Unit `StaffAlertsCheckTests` (13): New York and London at their own 20:30; clock-change days (New York 8 March and 1 November, London 25 October); someone clocked in after the set time is not told; default 21:00 dropped in quiet hours, not held; no consent and STOP; texts to the person only; long break once; one summary for the admins, email only; none when off or nobody is in; one organization failing.
    - Integration `StaffAlertsFlowTests` (4), with the database and the job as Hangfire starts it: **P6-3** on New York's clock-change day (London texted at 20:30 UTC, New York not at 00:30 UTC but at 01:30 UTC, nobody twice, no consent recorded as skipped); **P6-1** alerts off texts nobody; a long break texted once, a finished break not; the admins' summary, one email. `StaffAlertSettingsTests` also checks the quiet-hours line.
  - **Exit checks:** P6-1 (fields and alerts only when on) and P6-3 (two time zones, clock-change day): automated, with the stand-in. P6-2: the baseline suite.
  - **Results (full run, one suite at a time):** Unit 262, Architecture 11, **Baseline 314**, Integration 295, UI 13 — all passed.

- **2026-10-20 — Phase 6 / Step 6.2: the texts box on the staff and volunteer forms (C3-8; developer: "go ahead with 6.2").**
  - **Smallest change to the approved forms:** they already ask for a phone number (required), so only an unticked box is added, with `ConsentWording.StaffAlerts` (message types, "Msg & data rates may apply", "Reply STOP to opt out"). **C3-8, four lines in C1 files, one each:** `Pages/StaffAttendance.cshtml`, `Pages/VolunteerAttendance.cshtml` (kiosk registration), `Pages/Shared/_StaffEditorModal.cshtml`, `Pages/Shared/_VolunteerEditorModal.cshtml` (Manage Staff / Manage Volunteers, also used by the details pages). Each includes `Areas/Settings/Pages/Shared/_StaffSmsConsent.cshtml`, which renders **nothing** unless the organization switched staff alerts on. No page model, script or other markup of the approved app changed.
  - **Recording (new code):** kiosk registration posts the box with the form; `KioskConsentPageFilter` (a global page filter) records it after a registration that succeeded (the page's redirect to the person's clock page). The editors save by script; the partial's own script sends the box to `POST /api/staff-alerts/consent` (admins of the organization) after the editor's save succeeded, before the page reloads, and clears the box whenever the editor opens. `StaffConsentRecorder` records a grant for the organization, the person (`staff:{id}` / `volunteer:{id}`), the source and the wording, for the number saved on the person; only when staff alerts are on (a forged tick with alerts off records nothing).
  - **Country code:** a number without one cannot be texted: the box's script stops the form with a message; the server records nothing for such a number (the person is registered all the same). Withdrawal is by STOP (Phase 5).
  - **Tests:** Integration `StaffConsentTests` 4 (no box with alerts off on all four pages and a forged tick ignored; box shown unticked when on; a ticked kiosk registration recorded with its wording; no country code: registered, not recorded; the editors' endpoint only for the organization's own people). UI `StaffConsentBrowserTests` (the editor: country code required, recorded on save, cleared for the next person). The browser test first failed on its own data (an address under the editor's 10-character rule).
  - **Exit check P6-1 (fields appear only when on): automated.** P6-2 (forms unchanged with alerts off): the baseline suite, see the results.
  - **Results (full run on commit ac03567, one suite at a time, in a separate copy: the developer's app was running):** Unit 249, Architecture 11, **Baseline 314 (P6-2: the approved forms unchanged with alerts off)**, Integration 291, UI 13 — all passed.

- **2026-10-20 — Phase 6 / Step 6.1: staff alert settings (developer: "branch from mt/integration for phase 6").**
  - **Entry:** Phase 5 merged into `mt/integration` (fast-forward to `47b206a`, local; not pushed yet); `mt/phase6` from there. Phase 5's Twilio checks (P5-1, P5-4) still wait for credentials, as recorded. Sender registration (Phase 6 entry, before real US numbers) is not needed while texts go to the stand-in.
  - **Settings (C3-8, D32):** on `TenantMessagingSettings`: staff alerts on/off (**off**), missed clock-out time in the organization's time (**21:00**), long break (**60** minutes, 10 to 720), end-of-day summary to admins (off). `StaffAlertSettings` (Application) reads them (kept a minute; cleared on change) and saves them with an audit entry (`organization.staff-alerts-changed`).
  - **Back office (new files, no approved page edited):** `Areas/Settings/Pages/StaffAlerts` at `/Settings/StaffAlerts`, the organization's Owner only (like Billing; never in a support session), in the back-office layout. **C3 (`_Sidebar.cshtml`):** a "Staff alerts" link next to Billing, for the Owner only. When the organization is read-only, saving is refused like every other change.
  - **Migration M1p_StaffAlertSettings** (row 21): four columns, additive; existing rows get the D32 defaults (the generated defaults were 0 and 00:00 and were corrected in the migration). Rehearsed with an existing row; local database backed up (verified) **before** applying; app data unchanged.
  - **Tests:** Integration `StaffAlertSettingsTests` (defaults, save, refusals, audit, admins and the menu). The test first failed on its own helper picking the top menu's Sign out form, then on a hand-written form address that left out the form token; the page now uses the tag helper.
  - **Note:** the developer's app was running during this step (its build files locked), so builds and tests ran in a separate copy of the repository.

- **2026-10-20 — Phase 5 bundle and exit review.**
  - **Bundles:** `scripts/migrations/phase5-up.sql` and `phase5-down.sql`, generated by `build-phase5-bundle.ps1` from M1m, M1n and M1o, with the earlier guards (refusing a database without the Phase 4 schema: 50070; unknown migrations: 50071, 50072). CI checks they are up to date. Rehearsed (see `scripts/migrations/README.md`).
  - **Local database:** verified backup `DB_A65635_inpremdb_before_phase5_bundle_20261020.bak` first; the bundle found M1m to M1o applied and reported complete. Verify `M1m=1|4|2|2|0|0 M1n=1|1|0 M1o=1|1|0`; app data unchanged.
  - **Exit checks:**
    - **P5-1 Login codes:** reuse and expiry rejected, with the stand-in and with a pretend Verify (`TwilioClientTests`, `SmsCodeSignInTests`, `AdminSmsSignInTests`). **Waiting:** real Twilio Verify, when the project owner's Twilio test credentials come.
    - **P5-2 Consent: passes.** No text without consent; STOP stops every text at once, also one already queued; START resumes only what was agreed (`NotificationFlowTests`, unit tests).
    - **P5-3 Limits: passes.** Quiet hours in the organization's time zone (two zones and a clock-change day), the organization's switch and monthly limit, the platform's daily spend cap and its warning (unit and integration tests).
    - **P5-4 Delivery:** status and price recorded from Twilio's reports, simulated with signed reports (`NotificationFlowTests`). **Waiting:** "100% of sent messages on staging" needs Twilio and the test site.
  - **Phase 5 merged into `mt/integration`** (developer's request, before the Twilio checks), as Phases 1 to 4 were before their sandbox checks.

- **2026-10-20 — Phase 5 / Step 5.3: billing and provider alerts, console Notifications pages (developer: "push and go").**
  - **Alerts (`Application/Notifications/BillingAlerts.cs`, design Appendix C), each with an event key so a repeat sends nothing twice, and never failing what raised it:**
    - To the owners: trial ending (7 days and the day before; daily check), payment failed and suspended (`SubscriptionSync`, after the change is saved: webhooks and the daily check), invoice issued (**a text only**: the invoice email is the invoice's own, Inprem's or PayPal's), invoice overdue (daily check, once per invoice).
    - To Inprem: new signup (website signup; all roles), billing problems (Owners and Billing; at once, or one daily summary per the user's choice), system problems (Owners; urgent, also at night; at most one per kind per hour): a PayPal webhook that fails verification, a background job that failed for good (a Hangfire filter on this app's job server only), texts at 80 % of the daily spend cap (once a day).
  - **"My alerts" (console header, any role):** new signups, billing problems (each time / daily summary / off), system problems (Owners), and texts too, with a consent on the confirmed phone (`ConsentWording.ProviderAlerts`). Switching Inprem's texts off withdraws only Inprem's: **consent rule changed** so that only a STOP reply stops every text to a number (a unit test updated with the reason).
  - **Console → Notifications (Owners):** Delivery log (filters, paging; numbers masked), Message wording (list, edit on a separate page, switch off, back to the default), Opt-outs (masked), Texts and spend (today against the cap; each organization this month). "Send me a test message" on its own page (list pages have no forms). Organization → **Texts** (on/off, monthly limit; Owners, recent two-factor). The organization's Messages tab links to its log.
  - **Migration M1o_ProviderAlertPreferences** (row 20): one table, additive. Rehearsed; local database backed up (verified) **before** applying; app data unchanged.
  - **Tests:** Unit +11 (`BillingAlertsTests`; consent rule). Integration +12 (`BillingAlertsFlowTests` 5, `NotificationPagesTests` 5, signup alert, console role matrix and audit coverage). A test that sent texts at night found them rightly held for quiet hours: the test now moves its clock past the waiting time.
  - **Not covered by an automated test:** the failed-job alert end to end (the filter needs a running job server); it is a few lines that call the tested `SystemProblemAsync`.
  - **Results (full run on commit c22cedc, one suite at a time):** Unit 249, Architecture 11, Baseline 314, Integration 285, UI 12 — all passed.

- **2026-10-20 — Phase 5 / Step 5.2: login codes by text (developer: "continue with step 5.2"), with the stand-in.**
  - **Core (Application `LoginCodes.cs`):** `IOtpService` (Twilio Verify, or the stand-in until Twilio is set up) and `LoginCodeService`: at most 3 codes in 10 minutes and 10 a day per account (failed sends do not count), 5 wrong codes stop tries for 15 minutes (on top of Verify's own limits); every send and result in the audit log with the number masked, never the code.
  - **Infrastructure (`Twilio/OtpServices.cs`):** `TwilioVerifyOtpService` (Verifications and VerificationCheck; a used or expired code is gone at Verify: 404) and `StandInOtpService` (the same rules in memory: a 6-digit code works once, for 10 minutes, for 5 tries; saved to `Twilio:StandInFolder`, never logged). `Twilio:VerifyServiceSid` is required with `Twilio:Mode = Twilio`.
  - **Console (D14):** "My phone for codes" (header link; any role; add a number and prove it with a texted code; recent two-factor needed; remove) and "Text me a code instead" on the two-factor step, offered only with a confirmed phone. The authenticator app stays the main way.
  - **Organization admins (optional, off by default):** console → organization → **Sign-in codes** (Owners, recent two-factor). When on, `SmsSignInGate` (after the tenant middleware) sends the organization's admins to `/Account/SmsSignIn` until a code is approved; other requests are refused (403). Someone without a confirmed number adds one with the same code and is emailed about it. Support sessions are not asked. The proof is a claim tied to the membership, kept on cookie refresh. With the setting off nothing changes (baseline suite).
  - **Files:** new pages `Areas/Provider/Pages/LoginWithSmsCode`, `MyPhone`, `Tenants/SignInCodes`, `Areas/Account/Pages/SmsSignIn`; `Hosting/SmsSignInGate.cs`; `Application/Notifications/MessagingSettingsService.cs`. Changed: `LoginWith2fa` (the link), `_ConsoleLayout` (header link), `Tenants/Details` (button), `Hosting/TenancyHosting.cs` (claim kept), **C2-2** `Program.cs` (one line: the gate).
  - **Migration M1n_SmsSignIn** (row 19): one column, additive, off everywhere. Rehearsed; local database backed up (verified) **before** applying; app data unchanged.
  - **Locally:** `Twilio:StandInFolder` set in user-secrets to `Desktop\inprem-sms`: stand-in texts and codes are saved there.
  - **Tests:** Unit +9 (`LoginCodeServiceTests`, the settings switch); Integration +20 (`TwilioClientTests` Verify and stand-in rules, `SmsCodeSignInTests` 7, `AdminSmsSignInTests` 3); console coverage and role matrix updated.
  - **Exit checks:** P5-1 with the stand-in and the pretend Verify (reuse and expiry rejected); with real Verify when the credentials come.
  - **Results (full run on commit d533628, one suite at a time):** Unit 238, Architecture 11, Baseline 314, Integration 274, UI 12. One UI test (`ReadOnlyGuardBrowserTests`, from Phase 4) failed 3 times in 7: the page was there and complete, but the wait also waited for Google Fonts on a slow connection. It now waits for the address only (`WaitUntil = Commit`); 5 runs of 5 and the UI suite (12) pass.

- **2026-10-19 — Phase 5 / Step 5.1: the notification module (developer's "go"), built with the stand-in sender.**
  - **Entry deviation (developer's choice):** Phase 5 starts without Twilio test credentials. Everything Twilio-facing is built and tested against a pretend Twilio; the stand-in sender (`Twilio:Mode = StandIn`, the default) sends nothing. Real texts, P5-1 (Verify, step 5.2) and P5-4 on staging wait for the credentials from the project owner.
  - **Domain** (`Inprem.Platform.Domain/Notifications`): `Notification` (the outbox: Queued / Sent / Delivered / Failed / Skipped with a reason, attempts, Twilio's id and price, `NotBeforeUtc` for quiet hours, `EventKey` so an event never reaches a person twice), `ConsentRecord` (append-only: grants with their exact wording per organization; STOP stops every text to the number; START resumes only what was agreed), `NotificationTemplate` (console overrides of a wording, or switched off), `TenantMessagingSettings` (texts on or off, monthly limit), `PhoneNumber` (E.164 only; a number without its country code is refused, not guessed; masked in logs and the audit log).
  - **Application** (`Notifications`): `NotificationService.RaiseAsync` picks the wording (defaults in `NotificationTemplates`, step 5.1 has the test message only) and the recipients (owners, owners and admins, console users by role, or given), then per text: number, consent, the organization's switch and monthly limit (its own calendar month), the platform's daily spend cap (Twilio's price, else an estimate), quiet hours in the organization's time zone (21:00–08:00; urgent messages go at once). Emails need no consent and ignore quiet hours. `NotificationSender` (the send job) checks consent again before each text, retries temporary failures (5 attempts), and records Twilio's "unsubscribed" answer (21610) as a STOP. `TwilioReports` applies delivery reports (late or out-of-order ones never reopen a message) and STOP / START / HELP replies.
  - **Defaults agreed with the developer:** owners always get emails, texts only with consent; texts on for every organization, 50 a month each; US$5 a day for the platform (`Notifications:*`, checked at startup).
  - **Infrastructure:** `NotificationStore`, `RecipientDirectory` (memberships, console users, `AspNetUsers` phone and first name read-only, the organization's time zone); `Twilio/`: options checked at startup, `StandInSmsSender` (saves each text to `Twilio:StandInFolder` when set), `TwilioSmsSender` (Messaging Service, status callback; no client retries, so a text is never sent twice), `TwilioSignature` (X-Twilio-Signature, checked against Twilio's documented example).
  - **Web app (new files):** `Notifications/NotificationsHosting.cs` (registration; send job every minute, registered with the job server), `Notifications/TwilioWebhookController.cs` (`POST /webhooks/twilio/status` and `/inbound`, App site, signature required: 403 otherwise, bodies never logged). **C2-2:** one line in `Program.cs`. `Hosting/JobsHosting.cs` registers the send job.
  - **Website signup:** an owner who ticks the texts box with a mobile number gets a consent record with `ConsentWording.OwnerAlerts` (`SignupService`). **Follow-up for the web team:** the integration guide must give this exact wording for the box (not changed yet: waiting for the developer's yes). The demo website still sends `smsOptIn: false`.
  - **Migration M1m_Notifications** (row 18 in `scripts/migrations/README.md`): 4 new tables, additive. Rehearsed (up, up again, down, down again, up). Applied to the local database; app data unchanged. **The before-backup failed** (a path mistake in the command, the same as with M1k); a verified after-backup was taken.
  - **Tests:** Unit +55 (`NotificationDomainTests`, `NotificationServiceTests`), Integration +21 (`TwilioClientTests`, `NotificationFlowTests`: STOP ends every text at once and START brings them back, forged and wrong-path signatures refused, delivery status and price recorded, monthly limit and switch, the same event never twice; `SignupTests`: the ticked box is recorded with its wording).
  - **Exit checks so far:** P5-2 and P5-3 (automated). P5-4 with simulated reports; on staging when Twilio is set up.
  - **Results (full run on commit e4285de, 2026-10-20, one suite at a time):** Unit 229, Architecture 11, Baseline 314, Integration 260, UI 12 — all passed.

- **2026-10-18 — Fixes from the developer's live test on the SmarterASP test site.**
  - **Support sessions** (`/Account/SupportSessions`) showed the kiosk menu; it now uses the back-office layout, like Billing (fixed 2026-10-16).
  - **Invitations** (`/Account/Invitations`): **Continue** went to the kiosk (Volunteer Attendance); it now goes to the back office. Where **Accept** leads is unchanged (Choose organization with more than one organization), by the developer's choice.
  - **Tests:** Integration `SupportSessionTests` (the back-office menu) and `AccountRulesTests` (the Continue link) extended.
  - **Results:** Integration 239 passed; Baseline `AdminAccountTests` 4 passed.

- **2026-10-17 — Back office: a second manual clock-in on the same day is explained (developer's request).**
  - **Before:** on Staff Clockings and Volunteer Clockings, "Manual Clocking Here" for someone already clocked in today reloaded the page silently: nothing saved, nothing said. The kiosk says "You have already clocked in today."
  - **Now:** the page's existing pop-up ("Clocking not saved", the one used for "Clock Out must be after Clock In") says: "<name> has already clocked in today. To change today's times, use Edit on their row." Nothing is saved, as before.
  - **C1 files changed with the developer's approval** (as with `EmailService` and `BackOffice.cshtml.cs` earlier): `Pages/StaffClocking.cshtml.cs` and `Pages/VolunteerClocking.cshtml.cs`, a few lines each in the same-day check; the pages themselves are unchanged.
  - **Tests:** Baseline +2 (`ClockingCrudTests.Clocking_A_Second_Manual_Clock_In_The_Same_Day_Is_Explained_And_Not_Saved`, staff and volunteer; tagged `ChangedBy` as a deliberate change).
    - `Clocking_Manual_Entry_Rules` recorded the old silent reload for the second entry; it now expects the explanation (tagged `ChangedBy`).
  - **Results:** Unit 174, Architecture 11, Baseline 314, Integration 239, UI 12.

- **2026-10-17 — Fix from the developer's manual testing: console Sign out answered "HTTP ERROR 400".**
  - **Found:** the console checks a form token on every console page (step 0.7), Logout included. The token belongs to the sign-in the page was shown with. After the session timed out (30 idle minutes), or after signing in to the app in another tab of the same browser (one sign-in on a combined site), it no longer matched, and the sign-out was refused with 400 instead of reaching the sign-in page. The original app's own Logout solved the same case the same way (`Areas/Identity/.../Logout.cshtml.cs`).
  - **Fix:** the console's Logout page is `[IgnoreAntiforgeryToken]`, and the console's convention no longer adds its token check to a page that opts out explicitly (`ProviderHosting`). A forged sign-out can only sign someone out. Every other console form still needs its token.
  - **Tests:** Integration +4 (`ConsoleSignOutTests`): a normal sign-out, after the session timed out, and after signing in to the app in another tab all end on `/Provider/Login` (the last two failed before the fix with 400); a console form without a token is still refused (400).

- **2026-10-17 — Read-only guard on the back-office pages (developer's request: no filling in forms when the account is suspended).**
  - **What:** in a read-only organization (suspended by Inprem, or a lapsed subscription), a click on a button that would start a change (Add New, Add, Create, Edit (also the icon-only pencil, by its title), Save, Delete, Move To …, Clock In / Clock Out / Break Start / Break End, Register) shows a notice at once and opens nothing: "Your organization's account is suspended. You cannot add new staff." (the object follows the page: staff, volunteers, staff or volunteer clockings, admins). Forms that sit on the page (Create Admin Account, Manual Clocking) are greyed out with the same sentence above them. Search, filters, reports and exports are untouched; the server still refuses every change (unchanged).
  - **How:** a small script in `Pages/Shared/_PlatformBanner.cshtml` (our C3-2 partial, already in both back-office layouts), rendered only when the organization is read-only. It listens to clicks before the page's own handlers, so no frozen (C1) page was changed.
  - **Test:** UI +1 (`ReadOnlyGuardBrowserTests`, real browser: Add New and Edit show the notice and no editor; Search still works; the Create Admin Account form is greyed out with the note; the manual Clock In is stopped; after reactivating, Add New opens the editor again).

- **2026-10-17 — Suspension notices and the audit log's paging (developer's requests).**
  - **Suspending a paying customer** (the developer asked whether it is good practice: yes, for misuse, fraud, payment disputes, security or legal reasons, with safeguards; two were missing):
    - **Owners are emailed** on suspension (read-only, data safe, reports and exports still work, contact Inprem) and on reactivation. The reason goes into the email only when the provider ticks **Include the reason in the email to the owners** (off by default, e.g. for a fraud investigation); the audit entry notes when it was shared. An email that cannot be sent is audited (`notification.failed`) and shown as a warning; the change stands.
    - **The console warns** on the Suspend page when the organization pays through PayPal (Active or PastDue): PayPal keeps charging while it is suspended; cancel on the Subscription tab if it should stop; refunds are a separate decision.
    - **Project-owner document:** decisions 8 (billing during a suspension: pause, refund or credit, or cancel?) and 9 (when Inprem may suspend, in the terms of service), and item 13 (PayPal sandbox keys from Inprem's account: the developer cannot open a PayPal account from Ghana).
    - **Tests:** Integration +2 (`SuspensionNoticeTests`).
  - **Audit log paging:**
    - **Found while testing:** the Previous / Next links (and the CSV link with an action filter) had an empty address whenever the action filter was set: its address name `Action` is a route value MVC keeps for itself. The filter is now `act` in the address.
    - **Added:** numbered pages (the first, the last and two on each side of the current page, with gaps) and **Per page** 25 / 50 / 100 / 200 (any other value shows 50); the filters and the size travel with every page link.
    - **Tests:** Integration +1 (`AuditAndDashboardTests`: 60 entries in pages of 25, links keep the filter and size, last page, a bad size, the page-number pattern).
  - **On hold (developer, 2026-10-17):** the pretend-PayPal mode for local manual testing.
  - **The flaky demo-site browser test, found:** the sign-up and "Manage my subscription" pages attached their submit handler only after loading time zones (and the CAPTCHA widget). A click before that (the first test on a freshly started app) did a plain form submit: the page reloaded, nothing was sent, and the test waited. A quick real user could hit it too. **Fix:** the Register and Send-me-a-link buttons stay disabled until the page is ready. Three full UI runs passed, one of them on a busy machine.
  - **Results:** Unit 174, Architecture 11, Baseline 312, Integration 235, UI 11 (×3).

- **2026-10-17 — Fixes from the developer's manual testing of Phase 2 (test guide T2-3 and the Billing page).**
  - **A suspended organization was not read-only (T2-3).**
    - **Found:** suspending in the console (organization status Suspended) changed nothing: no banner, saving and the kiosk still worked. Step 2.7 based access on the subscription only, although `OrganizationStatus.Suspended` said "enforced as read-only from Phase 2 (C3-3)".
    - **Fix:** `EntitlementService` also reads the organization's status (`IEntitlementStore.IsSuspendedAsync`). Suspended by Inprem = read-only whatever the subscription says. Reports and exports still work, the kiosk pauses, and the banner and the read-only page say "Inprem has suspended this organization's account. Please contact Inprem." without the owner's Billing hint (paying would not lift it). Reactivating restores full access at once.
    - **Test:** Integration +1 (`EnforcementTests.An_Organization_Suspended_By_Inprem_Is_Read_Only_Until_Reactivated`, with an active paid subscription): failed on the old code, passes now.
  - **The Billing pages showed the kiosk menu.**
    - **Found:** `Areas/Billing/Pages/_ViewStart.cshtml` used `_MainLayout` (the kiosk layout, menu: Volunteer Clocking, Staff Clocking).
    - **Fix:** now `_Layout` (the back office, with Dashboard … Billing).
    - **Test:** `OrganizationInvoiceTests` checks the back-office menu on `/Billing`: failed on the old code, passes now.
  - **Browser tests run one class at a time** (`tests/InpremClockingApp.Tests.Ui/AssemblyInfo.cs`). The flaky failures (J3/J4 in the demo site, then the sign-in smoke test) were timeouts from all classes starting an app and a browser at once. Three full runs in a row passed, in about the same time (42 s). The demo-site tests also print the page's error if a sign-up fails.
  - **Results:** Unit 174, Architecture 11, Baseline 312, Integration 232, UI 11 (×3).

- **2026-10-16 — Phase 4 bundle and exit review.**
  - **Bundles:** `scripts/migrations/phase4-up.sql` and `phase4-down.sql`, generated by `build-phase4-bundle.ps1` from M1k and M1l, with the earlier guards (also refusing a database without the Phase 3 schema). CI checks they are up to date. Rehearsed on LocalDB (see `scripts/migrations/README.md`).
  - **Local database:** verified backup `DB_A65635_inpremdb_before_phase4_bundle_20261016.bak` (the backup path was checked this time, see step 4.1), then the bundle: M1l applied (M1k was there). Verify `M1k=1|2|1|4|2|0 M1l=1|1|2|0|0`; app data unchanged.
  - **Exit checks:**
    - **P4-1 Invoices:** created, sent, paid and reflected through PayPal's messages, all with the pretend PayPal (`InvoicePagesTests`, `InvoiceWebhookTests`, `OrganizationInvoiceTests`). **Waiting:** the same in the PayPal sandbox. Also to confirm there: that PayPal's payment id on a paid invoice is the capture id, and the ids Transaction Search returns (`paypal-notes.md`).
    - **P4-2 Offline payments and refunds: passes.** Role checks (Billing and Owners; Support refused), the Billing refund limit and Owners only at 0, a fresh two-factor code for refunds, and one audit entry per action (`InvoicePagesTests`, the role matrix, the audit coverage list).
    - **P4-3 Reconciliation:** the monthly report matches the console and PayPal by id, with totals and every difference, and exports CSV, all with the pretend PayPal (`MonthlyReportTests`). **Waiting:** "console totals equal PayPal sandbox totals for a test month" needs a month of sandbox transactions.
    - **Build-plan tests:** invoice lifecycle with the pretend PayPal; role checks, re-authentication and audit; the page-pattern test now also covers the Invoices and Reconciliation lists (no action forms); isolation: an organization sees only its own invoices (owner page, invoice page 404 for another organization or a draft, website list). The route-isolation suite covers the new routes from the endpoint table.
  - **Results:** Unit 174, Architecture 11, Baseline 312, Integration 231, UI 11. No model drift.

- **2026-10-16 — Phase 4 / Step 4.4 — Organizations see their invoices.**
  - **`InvoiceService.ForOrganizationAsync`:** an organization's own invoices, newest first, never drafts. Statuses use the website API's words: Sent, PartiallyPaid, **Overdue** (still to pay after the due date), Paid (also when part was given back, with the amount refunded), Refunded, Cancelled. The PayPal payment link appears only while something is owed on a PayPal invoice.
  - **In-app Billing page (owners):** an **Invoices** section (number, issued, due, total, to pay, status, **Pay at PayPal**; an offline invoice says to quote its number). Each number opens **`/Billing/Invoices/{id}`**: the invoice, or a **receipt** once paid, with lines, payments received, refunds and **Print or save as PDF**. Another organization's invoice, or a draft, is not found; admins who are not the owner cannot open it; support sessions cannot reach Billing pages (as before).
  - **Website API:** `GET /account/invoices` returns the same list (draft `Invoice` schema, plus an optional `amountRefunded`, recorded in the draft and the integration guide as change 4 to confirm with the web team).
  - **Tests:** Integration +2 (`OrganizationInvoiceTests`: pay links, overdue, paid with refund, receipt, offline instructions, drafts and Beta's never shown, 404 for them, a non-owner admin refused; the website list for a billing link, only its own organization).

- **2026-10-16 — Phase 4 / Step 4.3 — Invoice webhooks and the monthly reconciliation report.**
  - **`InvoiceSync`** (Application):
    - **Invoice events** (`INVOICING.INVOICE.PAID`, `.CANCELLED`, `.REFUNDED`, `.UPDATED`, …): PayPal is re-read. Payments through PayPal are recorded once by PayPal's id (`payment.received`), refunds once by refund id, a cancellation at PayPal cancels ours (nothing paid), and a lost send answer marks the draft sent.
    - **Refund events** now record one row per refund: `PAYMENT.SALE.REFUNDED` (subscriptions) and `PAYMENT.CAPTURE.REFUNDED` (invoices). This fixes the Phase 2 behaviour (see step 4.2), and the old `Payment.Refund` (last amount as total) is removed. A refund made from the console and then reported by PayPal stays one row; a pending refund that completes or fails is updated (`payment.refund-updated`).
    - **`PAYMENT.CAPTURE.REVERSED`** marks the invoice payment reversed (`payment.reversed`) and reopens the invoice.
    - **Daily check:** `BillingReconciliation.DailyAsync` also re-reads every unfinished PayPal invoice; corrections go into the provider summary.
  - **Monthly report** (`MonthlyBillingReport`, P4-3): the console's PayPal payments and refunds for a UTC month against PayPal's Transaction Search (new `IInvoiceProvider.SearchTransactionsAsync`), matched by PayPal ids.
    - Results per line: Matched, AmountDiffers, OnlyInConsole, OnlyAtPayPal; offline money apart; chargebacks for information; fees and transfers left out.
    - Totals per currency with **Equal / Different**; differences listed first.
    - If PayPal cannot be asked, the report says so and shows the console's side.
    - **CSV** for accounting: UTF-8 with BOM, text cells protected against spreadsheet formulas, amounts as plain numbers.
  - **Console:** Billing gets a third tab, **Reconciliation**: month picker (the previous month by default), totals, differences, all lines, **Download CSV**. Owners and Billing; read only.
  - **No migration in this step.** PayPal notes updated (events, Transaction Search, what to confirm in the sandbox).
  - **Tests:**
    - Integration +9:
      - `InvoiceWebhookTests` (6): paid at PayPal recorded once over three messages; capture refunds 20 + 5 = 25 and a repeat changes nothing; sale refunds 10 + 5 = 15, then a console refund reported by PayPal stays one row (19); cancelled at PayPal; a chargeback reopens the invoice; a dropped message corrected by the daily check.
      - `MonthlyReportTests` (2): September matched, differs, only-here, only-at-PayPal, offline, chargeback, a transfer and a denied payment left out, totals and Different, CSV lines, the page and its CSV download, a bad month → 400; PayPal unreachable → said so.
      - Transaction Search client (1): all pages, dates, filter, no payer data in logs, more than 31 days refused.
    - Role matrix +1 page.
    - Unit: the payment test uses the refunded total instead of the removed method.
  - **Results:** Unit 174, Architecture 11, Baseline 312, Integration 229, UI 11. No model drift.

- **2026-10-16 — Fix from the developer's manual testing: console "+ New …" buttons readable without hovering.**
  - **Found:** the button-style links (**+ New organization**, **+ New plan**, **+ New invoice**) showed their text only on mouse hover. The console's general link colour (`.pc a`, blue) is more specific than `.pc-btn-primary`, so the text was blue on the blue button; only the hover rule won.
  - **Fix** (`wwwroot/provider/console.css`, our own console style sheet): link buttons keep the button's text colour (white on primary, normal on plain, red on danger), also on hover, with no underline. Covers all 31 console pages with link buttons.
  - **Test:** UI +1 (`ConsoleStyleTests`, a real browser computes the colours): it failed on the old style sheet (text `rgb(37, 99, 235)` on background `rgb(37, 99, 235)`) and passes now.

- **2026-10-16 — Phase 4 / Step 4.2 — Console invoices, offline payments and refunds (platform migration M1l).**
  - **`InvoiceService`** (Application). Every action writes one audit entry with the organization. PayPal is asked first, so a PayPal failure changes nothing here and is audited as failed.
    - **New invoice:** organization, PayPal or offline, up to 8 lines, due date, note. **Review** checks without saving; **Create and send** saves and sends.
      - A **PayPal invoice** goes to the organization's active Owner email. PayPal emails it, and the payer page link is kept. If PayPal is unreachable, the invoice stays a draft and **Send** on its page finishes it.
      - An **offline invoice** is emailed by us with its lines and total.
    - **Send again:** a draft is sent, a sent invoice is reminded (`invoice.resent`).
    - **Cancel:** only without payments (refund instead), with a reason, also at PayPal.
    - **Record offline payment:** date, method (bank transfer, cash, cheque, mobile money, other), reference (required and unique, so the same bank reference cannot be recorded twice), amount up to what is still to pay. It is also recorded on the PayPal invoice. The invoice status follows: PartiallyPaid, then Paid.
    - **Refund:** a PayPal payment is refunded through PayPal (a subscription payment as a sale, an invoice payment as a capture), with a request id derived from the payment and amount, so a double click refunds once. An offline payment is recorded as returned (and on the PayPal invoice). The invoice follows: PartiallyRefunded or Refunded.
      - **Owners refund any amount; Billing users up to `Billing:RefundLimit`** (D28; default 0 = Owners only).
      - The page needs a **fresh two-factor code** (`[RequireRecentTwoFactor]`, design 5.5) and asks for confirmation.
  - **One row per refund (`PaymentRefund`, migration M1l).** Found while building this: the Phase 2 webhook stores a refund's amount as the payment's refunded total, so a second partial refund would replace the first. Refunds are now rows, and the payment's total is their sum (`Payment.SetRefundedTotal`). M1l gives each payment refunded before one row with its total. The webhook moves to the rows in step 4.3 (PayPal refund ids are unique, so its repeats change nothing).
  - **Pages** (Owners and Billing): Billing has two tabs, **Subscriptions** and **Invoices** (list with filters for status, kind and issue dates, and **+ New invoice**). Also the invoice page (total, paid, still to pay, lines, payments with **Refund**, refunds, **Send / Send again**, **Record offline payment**, **Cancel invoice**, **Open at PayPal**), the Cancel, Record payment and Refund pages, and on the organization's **Invoices and payments** tab its invoices, **+ New invoice** and **Refund** per payment.
  - **Migration M1l:** `platform.PaymentRefunds`. Scripts rehearsed on LocalDB after the Phase 0 to 3 bundles and M1k, with an old refunded payment: up, up, down, down, up; verify `1|1|2|0|1`, and the old refund got exactly one row. **Not applied to the local database yet** (nor will later ones be), while the developer tests Phases 1 to 3 on it; M1k is already there.
  - **Tests:**
    - Unit +4: refund rules and totals.
    - Integration +7 (`InvoicePagesTests`): review saves nothing; created and sent by PayPal to the owner, with audit; an offline payment on a PayPal invoice is recorded at PayPal; PayPal unreachable → draft, then sent, then reminded; offline invoice emailed, paid in two parts, a reference refused twice, more than owed refused, cancel refused once paid; cancel at PayPal; an Owner refund needs a fresh code, and two partial refunds add up (10 + 5 = 15); the Billing limit (60 refused, 50 allowed), Owners only with a limit of 0, Support cannot open the page; an offline refund moves the invoice to PartiallyRefunded.
    - Role matrix +6 pages; audit coverage +7 handlers.
  - **Configuration:** `Billing:RefundLimit` (read at first use, so host and test settings apply).
  - **Results:** Unit 174, Architecture 11, Baseline 312, Integration 220, UI 10 (run group by group with the developer's app closed; an earlier full run was stopped for low memory). No model drift.

- **2026-10-16 — Fix from the developer's manual testing (test guide T1-3): accepting an invitation now offers "Choose organization".**
  - **Found:** an admin of one organization accepted another organization's invitation (`/Account/Invitations` → Accept) and stayed in the first one, without the choice. The accept handler refreshed the sign-in, which keeps the current organization (by design of the step 0.3 refresh), and then went to the kiosk page.
  - **Fix** (`Areas/Account/Pages/Invitations.cshtml.cs`, our own Phase 0 page): after accepting, someone with more than one active organization goes to `/Account/ChooseOrganization`; with one, to the kiosk page as before.
  - **Test:** Integration +1 (`Account_Accepting_A_Second_Organization_Offers_The_Choice_At_Once`): accept, the chooser lists both, choosing Beta shows Beta's staff and not Alpha's. It failed on the old code and passes on the new.
  - **Test guide:** rewritten with click-by-click directions from the real pages (developer's feedback); T1-3 describes the problem until the fixed build is running; T1-4 says who sends the console invitation.
  - **Two tests hardened (they failed on a busy machine, while the developer ran the app):**
    - `ApiSkeletonTests.Idempotency_…`: a duplicate that arrives while the first is still running waits up to 10 s, then answers 429 "still being processed" (by design, design 7.7). The test now retries a 429 as a client would, and still checks that the record was made once.
    - `DemoSiteBrowserTests`: Playwright waits up to 30 s instead of 5 s. `J3_J4_…` failed once more after this, at 47 s, without its message captured; it passed alone and in the next full UI run. **Watch:** if it fails on a quiet machine, keep the full log (`--logger "console;verbosity=normal"`).
  - **Results:** Unit 170, Architecture 11, Baseline 312, Integration 213, UI 10.

- **2026-10-16 — Phase 4 / Step 4.1 — Invoicing client and invoice records (platform migration M1k).**
  - **Checked first:** PayPal's Invoicing v2 description (version 2.12.0) and webhook event names; endpoints, retry rules, statuses, methods and refunds are recorded in `docs/multitenancy/paypal-notes.md`.
  - **`IInvoiceProvider`** (Application) and **`PayPalInvoiceProvider`** (typed client on the shared `PayPal` HttpClient):
    - create a draft (our number; a repeat finds PayPal's first invoice by number), send (returns the payer's page), read, cancel (a draft is deleted), record offline payments and refunds;
    - give money back: a subscription payment as a PayPal sale (v1), an invoice payment as a capture (v2), both with our `PayPal-Request-Id`;
    - calls PayPal cannot make safe to repeat (create, send, recorded payments and refunds) are never retried by the pipeline (`PayPalRest.NoRetry`);
    - errors carry PayPal's error code (`PaymentProviderException.Issue`), never values; payloads are never logged.
    - The REST helpers of step 2.2 moved to `PayPalRest`, shared by both clients (no behaviour change; its tests still pass).
  - **Domain `Invoice`** (with `InvoiceLine`): sources Subscription, PayPalInvoice and Manual; Draft → Sent → PartiallyPaid / Paid → PartiallyRefunded / Refunded, or Cancelled (only without payments). The status follows the amounts paid and refunded. Numbers `INV-100001`, … from a database sequence; 1 to 100 lines; amounts with at most two decimals.
  - **`Payment`** gets `InvoiceId` and `Method` (`OfflinePaymentMethod`: bank transfer, cash, cheque, **mobile money**, other; PayPal has no mobile money, so it is sent as OTHER).
  - **`IInvoiceStore`:** next number, add, find (by id or PayPal id), list per organization, save; a second invoice for the same subscription period that is not cancelled is refused (`DuplicatePeriod`).
  - **Migration M1k:** `platform.Invoices`, `platform.InvoiceLines`, sequence `platform.InvoiceNumbers` (starts at 100001, above the old billing code's numbers), the two payment columns, foreign keys to `dbo.Tenants` and the platform tables. Scripts `platform-M1k-up/down/verify` rehearsed on LocalDB (after the Phase 0 to 3 bundles: up, up, down, down, up; verify `1|2|1|4|2|0`).
    - **Found in the rehearsal:** the old app's `dbo.Invoices` uses two of the same foreign-key names, so the verify script counts foreign keys in the platform schema only.
  - **Local database:** M1k applied; verify `1|2|1|4|2|0`; app data unchanged (fingerprint). **The backup before applying failed** (a wrong file path in the command; nothing was written), and this was only noticed after applying. A verified backup was taken right after: `DB_A65635_inpremdb_after_M1k_20261016.bak`. Before M1k, the database is reachable with `platform-M1k-down.idempotent.sql` (rehearsed) or the `before_M1j` backup of 2026-10-15.
  - **Test hosts:** `FakeInvoiceProvider` (in memory; pay by PayPal, fail next) on every app fixture (`Invoicing`).
  - **Tests:**
    - Unit +8: numbers, lines and totals, sending, the status from the amounts, cancelling, payments linked with their method.
    - Integration +30: the client against a pretend PayPal (the body sent, a repeated create, no retry for create and recorded payments, retried refunds with one request id, sale and capture refunds, send already sent, reading, cancel draft / sent / cancelled, offline payments and refunds, all 16 statuses, not configured, no payer data in logs); the store (numbers unique and from 100001 when 20 are taken at once, lines saved, payments linked, one per period unless cancelled, a real organization required).
  - **Results:** Unit 170, Architecture 11, Baseline 312, Integration 212, UI 10. No model drift.
    - **Flaky under load (watch):** in the first full run, while the developer was running the app on the same computer, `ApiSkeletonTests.Idempotency_…` got one 429 (it ran 10 s instead of 2 s) and two UI tests timed out (`J3_J4_…`, `Ui_Kiosk_Volunteer_Clock_In_Button`). All three passed on a rerun. If one repeats on a quiet machine, look at it.

- **2026-10-15 — Phase 3 bundle and exit review.**
  - **Bundles:** `scripts/migrations/phase3-up.sql` and `phase3-down.sql`, generated by `build-phase3-bundle.ps1` from M1h, M1i and M1j, with the earlier guards (also refusing a database without the Phase 2 schema). CI checks they are up to date. Rehearsed on LocalDB (see `scripts/migrations/README.md`); a no-op on the local database.
  - **Exit checks:**
    - **P3-1 Journeys:** J1 to J5 pass through the demo website in a real browser with the pretend PayPal (`DemoSiteBrowserTests`). J6's outcome (a Failed checkout, organization not activated, provider alerted) is covered by the checkout tests. **Waiting:** J1–J6 with the PayPal sandbox and the web team's site on staging.
    - **P3-2 Contract: passes.** The OpenAPI document is generated and published (staging only); the contract test checks every generated operation against the agreed draft (operation ids and response codes) and the problem codes exactly. The integration guide's examples use the code's field names; running them as tests is left to the staging run.
    - **P3-3 Abuse protection: passes.** CAPTCHA required on signup and billing-access requests; 429 with Retry-After; billing-access answers identical for known and unknown emails (status, body, timing within 250 ms).
    - **P3-4 Idempotency: passes.** A repeated key creates exactly one record (signup, checkout, also at the same moment); stored answers encrypted.
    - **P3-5 Token scope: passes.** Signup and billing tokens reach only their own signup, checkouts and organization (404 otherwise); expired tokens rejected; the tenant-isolation suite covers every new route.
    - **P3-6 Redirect safety: passes.** Return and cancel URLs off the allow-list (or plain http other than loopback) are rejected.
    - **P3-7 Performance:** **waiting.** The load test (public reads p95 < 300 ms, signup p95 < 1 s at 20 rps) runs on the test site.
    - **P3-8 Privacy: passes.** No personal data in public responses (automated check).
    - **P3-9 Cookie independence: passes.** Cookie-only requests are refused with `token_missing`; CORS only for the website origins, without credentials.
  - **Results:** Unit 162, Architecture 11, Baseline 312, Integration 182, UI 10 (last full run, step 3.6; this entry changes scripts and documents only).

- **2026-10-15 — Phase 3 / Step 3.7 — Web team handover.**
  - **`docs/multitenancy/api/integration-guide.md`:**
    - the rules (tokens and their lifetimes, cookies ignored, CORS, idempotency, rate limits, every problem code with its meaning);
    - the public reads with an example;
    - journeys **J1 to J6** with request and response examples taken from the code's field names, the purpose table, and the polling rules and texts (design 7.5);
    - **what the website must never do**; CAPTCHA (Turnstile, test keys); the PayPal sandbox;
    - **staging notes**: base URL, docs, demo site, the values Inprem gives, support with the `traceId`;
    - the three changes from the draft, to confirm.
  - `docs/multitenancy/api/README.md` points the web team to the guide first.

- **2026-10-15 — Phase 3 / Step 3.6 — Demo website page (D29).**
  - **`wwwroot/demo-site/`**: plain HTML and `fetch`, no build step, only the public API. Pages:
    - pricing (`GET /plans`);
    - sign-up (time zones, slug check, trial or subscribe; one idempotency key per page view; subscribe continues with `POST /checkout-sessions` and goes to PayPal);
    - `return.html` (confirm, then polls 2 s for 30 s and 10 s up to 2 minutes, with the design 7.5 texts);
    - `cancel.html`;
    - "Manage my subscription" (`manage.html` asks for a link; `billing.html` takes the code off the address bar, shows the subscription, pays: convert, resubscribe or change plan, and cancels).
    - Tokens stay in the tab's `sessionStorage`. How to run it locally: `wwwroot/demo-site/README.md`.
  - **Off by default:** `Demo:EnableDemoSite`. A gate before the static files answers 404 for every `/demo-site` address when it is off, or on an Admin-only site.
    - When on: `/demo-site/config.json` (the public Turnstile site key or a test answer) and `/demo-site/billing` (forwards to `billing.html`, matching the billing links).
    - The demo is served from the app's own origin, so no CORS entry is needed.
  - **Checkout addresses:** plain http is now accepted for this computer's own address only (loopback: local runs and the browser tests). The PayPal allow-list still accepts https origins only.
  - **Tests:** UI +5 (`DemoSiteBrowserTests`, Playwright, real browser; PayPal's approve page is played by the test, which marks the subscription as PayPal would and redirects back):
    - **J1:** trial signup, then the email link and a password.
    - **J2:** subscribe, PayPal, return, Active.
    - **J5:** cancel at PayPal → Cancelled.
    - **J3 and J4:** an owner asks for a link, opens it, converts the trial through PayPal, then cancels.
    - Off unless switched on (404 for the pages and config).
    - Unit: loopback http accepted, other http refused.
  - **Results:** Unit 162, Architecture 11, Baseline 312, Integration 182, UI 10.

- **2026-10-15 — Local database: platform migration M1j applied.** Backup `DB_A65635_inpremdb_before_M1j_20261015.bak` taken first and verified; verify `1|1|1|0`; app data fingerprint unchanged.

- **2026-10-15 — Phase 3 / Step 3.5 — Existing customers (J3, J4; D27).**
  - **`POST /billing-access/requests`** (anonymous; CAPTCHA; write rate limit): checks the email's form and the CAPTCHA (the same for every email), then always queues the same background job and answers **202 with the same body**, in **at least 400 ms**.
    - **Nothing in the request depends on whether the email exists**: the lookup happens in the job (`BillingAccessJob`, Hangfire).
    - The job: for an active **Owner** of one or more organizations, one code per organization (at most 3 per organization per hour) and one email with a link per organization to `{Links:WebsiteBaseUrl}/billing?code=…`. Other emails (unknown, or admins who are not owners): nothing sent, nothing stored.
  - **`POST /billing-access/sessions`:** exchanges the code (single use, 15 minutes; `BillingAccessCode`, migration **M1j**, stored as a hash) for a 30-minute billing-access token for that organization, with its name. A bad, used or expired code → 400 `invalid_code`.
  - **`GET /account/subscription`** (billing token): status, plan summary, billing mode, trial end, period end, cancel-at-period-end, outstanding balance.
  - **`GET /account/invoices`:** empty until Phase 4.
  - **`POST /account/subscription/cancel`:** the Phase 2 cancel (PayPal first; access to the period end) → status, cancel-at-period-end, access end. Nothing to cancel → 409 `subscription_not_cancellable`; PayPal down → 502.
  - **`POST /account/subscription/keep`:** 409 `cannot_keep` (see the follow-up).
  - Audited: `billing-access.link-sent`, `billing-access.session-started` (per organization; never the email of an unknown request).
  - **Test host:** the billing-link queue keeps the jobs and the test runs them, as the job server would, so timing is measured on the real request path.
  - **Tests:** Integration +3 (`BillingAccessTests`):
    - **P3-3:** owner, admin and unknown email get the same status and body; all take at least 400 ms, known and unknown within 250 ms of each other. Only the owner gets a link (to the website), only one code is stored, as a hash. Bad email and failed CAPTCHA give their codes.
    - The code works once, expires after 15 minutes, at most 3 links an hour.
    - The billing token reads its organization's subscription, empty invoices, cancels (then 409), keep (409), and a signup token is refused.
  - **Results:** Unit 162, Architecture 11, Baseline 312, Integration 182, UI 5. No model drift.

- **2026-10-15 — Phase 3 / Step 3.4 — Checkout endpoints (J2, J5).**
  - **`POST /checkout-sessions`** (signup or billing-access token; `Idempotency-Key`): body `planPriceId`, `purpose`, `returnUrl`, `cancelUrl`. Calls the Phase 2 `CheckoutService.StartAsync` for the token's organization. Answers 201 `{ checkoutSessionId, approvalUrl, expiresAt }`.
    - A signup token checks out as `new` (the signup's organization, whose subscription waits as PendingActivation). A billing token checks out as `convert`, `resubscribe` or `change_plan`. A mismatch gives `validation_failed` on `purpose`.
    - **Codes:** `return_url_not_allowed` (not an allow-listed https origin); `plan_not_available` (not offered or not published to PayPal); `validation_failed` (purpose does not fit the state); 422 `idempotency_key_reused`; **502 `payment_provider_unavailable`**, safe to retry with the same key.
    - The checkout's own idempotency key is the token id plus the header, so two tokens never share one.
  - **`GET /checkout-sessions/{id}`, `POST …/confirm`, `POST …/cancel`:** only the token that started the checkout (anything else is 404). Answers: status, subscription status, plan summary (plan, price, cycle, amount), next billing date once Active, and the design 7.5 message. Confirm re-reads PayPal (idempotent, as all sources). Cancel marks it Cancelled, or returns the final status if it already ended.
  - **Phase 2 changes:**
    - `CheckoutService` now supports `new` (`StartCheckout.SignupId`; refused unless the subscription waits as PendingActivation).
    - Every refusal carries a `CheckoutFailure` reason; `FindAsync` was added.
    - `CheckoutSession` allows `new` with its signup's organization.
    - **A checkout that never reached PayPal gives its idempotency key back** (`ReleaseKey`), so the API's "retry with the same key" works. It stays Failed and audited.
  - **Tests:**
    - Unit: the input rule for `new` with an organization.
    - Integration +5 (`CheckoutApiTests`): J2 end to end (201, the same key returns the same checkout, AwaitingApproval until PayPal confirms, then Active with the next billing date and Active in the database, nothing left to pay after); other tokens get 404; every refusal code; J5 cancel (twice; the organization unchanged); a PayPal outage → 502 then 201 with the same key; a billing token converts a trial.
  - **Results:** Unit 162, Architecture 11, Baseline 312, Integration 179, UI 5. No model drift.

- **2026-10-15 — Local database: platform migration M1i applied.** Backup `DB_A65635_inpremdb_before_M1i_20261015.bak` taken first and verified; verify `1|1|1|0|0`; app data fingerprint unchanged.

- **2026-10-15 — Phase 3 / Step 3.3 — Registration (J1, J2).**
  - **`POST /signups`** (anonymous; the write rate limit; `Idempotency-Key`; CAPTCHA):
    - **Checks:** field errors keyed by JSON path (organizationName, slug, timeZone, owner.firstName, owner.lastName, owner.email, owner.mobile in E.164, both consents, planPriceId, mode, captchaToken); CAPTCHA → `captcha_failed`; price not public or not offered, or a trial on a plan without one → `plan_not_available`; slug taken → 409 `slug_taken`; owner email with an account → 409 `email_in_use`.
    - **One transaction** (the console's `ProvisionAsync`, extended): organization; owner account without a password; Owner membership; profile **PendingVerification**; subscription (trial: Trialing for the plan's trial days; subscribe: **PendingActivation without access** until PayPal approves, `Subscription.StartAwaitingPayment`); the **`Signup`** row (migration **M1i**).
    - Then a signup token (2 hours, linked to the organization) and the verification email.
    - **Answers:** 201 with `signupId`, the token, its expiry, `PendingVerification` and `nextAction` (`verify_email` or `checkout`). No personal data.
  - **`GET /signups/{id}`** and **`POST /signups/{id}/resend-verification`** (signup token of that signup only; another signup is 404): status and subscription status; at most 3 emails per hour (`resend_limit_reached`); `already_verified`.
  - **Verification page `/Account/VerifyEmail`** (App site, anonymous): the owner confirms the email (Identity's confirmation token, 24 hours) and chooses a password.
    - The password is checked first, so a weak one does not use up the link.
    - Then the organization becomes **Active** (`signup.verified` audited). Back-office access is possible only after this (D22): before, the owner has no password.
  - **CAPTCHA:** `ICaptchaVerifier`, Cloudflare Turnstile in Infrastructure (`Captcha:Secret`; fails closed); a test double in the test host.
  - **Stored idempotent answers are now encrypted** (data protection): the signup answer contains the token.
  - **Domain:** `OrganizationStatus.PendingVerification`; `TenantProfile.CreatePendingVerification` and `Verified`. PendingActivation has access only once PayPal approved (so an unpaid signup has none).
  - **Tests:**
    - Unit +1: awaiting payment has no access until PayPal approves.
    - Integration +6 (`SignupTests`):
      - J1 end to end: signup, everything created as described, status polling, email link, weak password refused, verified, Active, the owner signs in, the link done;
      - J2 waits for payment without access;
      - every error code;
      - idempotency (one organization) with the stored answer encrypted;
      - token scope (another signup 404, no token, wrong kind) and expiry after 2 hours;
      - resend limit and already verified.
  - **Results:** Unit 162, Architecture 11, Baseline 312, Integration 174, UI 5. No model drift.

- **2026-10-15 — Phase 3 / Step 3.2 — Public read endpoints.**
  - **`GET /plans`:** public, offered plans with their active prices (cheapest first): id, name, description, trial days, features (one per line in the console), `limits.maxPeople`, and prices as `{ amount: "29.00", currency }`. Cached for 5 minutes in the app and by the browser (`Cache-Control: public, max-age=300`).
  - **`GET /timezones`:** the same IANA list as the console (common ones first), with the current offset in the display name.
  - **`GET /slugs/{slug}/availability`:** available or not, with a free suggestion when the name is taken or reserved. A badly formed name gets 400 `validation_failed` with the field message.
  - **Problem details no longer echo the request path** (`instance` is the endpoint's pattern). Found by the tenant-isolation suite: it calls every route with another organization's values, and the 400 repeated them back. It was the caller's own input, not a leak, but errors should not repeat input.
  - **Tests:**
    - Integration +4 (`PublicReadTests`): plans (only public and offered; prices, features, limits; cache header; cached for 5 minutes); time zones; slug free, taken (with a suggestion that is itself free), reserved, badly formed (400, input not echoed).
    - **P3-8 privacy check:** no email, admin name or staff email in any public answer.
    - The contract test now covers four operations; the isolation suite covers the new routes.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 168, UI 5.

- **2026-10-15 — Phase 2 merged; Phase 3 started.** `mt/phase2` fast-forwarded into `mt/integration` (`9ac52c3`) and pushed, at the developer's request (P2-1 and P2-4 wait for the PayPal sandbox run). Branch `mt/phase3` from there.

- **2026-10-15 — Local database: platform migration M1h applied.** Backup `DB_A65635_inpremdb_before_M1h_20261015.bak` taken first and verified; verify `1|2|1|0|0`; app data fingerprint unchanged.

- **2026-10-15 — Phase 3 / Step 3.1 — Website API skeleton.**
  - **`/api/public/v1`** (minimal API group, `InpremClockingApp/PublicApi`): mapped only where the App site is served, and on its host when one is set. Step 3.1 has `GET /health`.
  - **Tokens:** `ApiToken` (migration **M1h**) for signup tokens (2 hours) and billing-access tokens (30 minutes).
    - The value is 32 random bytes, returned once; only its SHA-256 hash is stored.
    - `PublicApiTokens` issues and checks them. The `PublicApiToken` authentication scheme answers 401 with `token_missing`, `token_invalid` or `token_expired`; a token of the wrong kind is answered as invalid.
    - **The back-office cookie is ignored:** every protected endpoint's policy names the token scheme. The tenant-context and read-only middleware skip the API. Tested with a signed-in browser.
  - **CORS:** only `Site:PublicWebsiteOrigins`, GET and POST, the needed headers, `Retry-After` exposed, no credentials.
  - **Rate limits** (built-in limiter, one-minute windows):
    - per address by default; per token with a bearer token; a higher limit per valid `X-Api-Key` (`PublicApi:ApiKeyHashes`, stored as hashes); per address for the write endpoints;
    - 429 `rate_limited` with `Retry-After`.
  - **Problem details:** `application/problem+json` with a stable `code`, `traceId`, and a type URL per code. Unexpected exceptions become a 500 `internal_error` with no details; the code was added to the draft (see the follow-up).
  - **Idempotency** (`IdempotencyFilter`, `IdempotencyRecord` in M1h): the `Idempotency-Key` header is required.
    - Same key and body within 24 hours → the first answer again, without running twice (also at the same moment). Same key, other body → 422 `idempotency_key_reused`.
    - 5xx answers are not kept, so a retry is safe. A second request while the first is still running waits up to 10 seconds, then gets 429.
  - **OpenAPI** from the code (Swashbuckle 6.9.0) at `/api/public/docs` when `PublicApi:EnableDocs` (staging only).
  - **Contract test:** every generated operation is in the agreed draft with the same operationId and only its response codes; the problem codes are exactly the draft's list.
  - **`IPublicApiEndpointContributor`:** lets tests add endpoints inside the real API group (no app use).
  - **Tests:** Integration +7 (`ApiSkeletonTests`): health; tokens (missing, invalid, wrong kind, billing scope, expiry after 30 minutes, stored only as hashes, cookie-only refused); CORS (website allowed, others not, no credentials); 429 with Retry-After and the API key limit; idempotency (same answer, one run, 5 at once, 422, 5xx retried, free after 24 hours); a 500 without details; OpenAPI published and matching the draft.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 164, UI 5. No model drift.

- **2026-10-15 — Phase 2 bundle and exit review.**
  - **Bundles:** `scripts/migrations/phase2-up.sql` and `phase2-down.sql`, generated by `build-phase2-bundle.ps1` from M1f, M1g and M5. They have the earlier guards and also refuse a database without the complete Phase 1 schema. CI checks they are up to date.
    - **Rehearsed** on LocalDB with synthetic old billing rows (see `scripts/migrations/README.md`).
    - **Found:** a second down failed because SQL Server compiles skipped statements, and M5's named the M1f columns. M5's SQL now goes through `EXEC`; its scripts were regenerated and the rehearsal repeated cleanly. M5 is also now verify-safe when the payments table is gone.
    - **Local database:** `phase2-up.sql` ran as a no-op; guards passed; verify `M1f=1|3|2|0|2 M1g=1|0|0 M5=1|2|2|2|1|1|0|1`; app data unchanged.
  - **Exit checks:**
    - **P2-1 Checkout:** passes with the pretend PayPal (`BillingPageTests`: subscribe from the in-app Billing page, return, ACTIVE, Active). **Waiting:** the same in the PayPal sandbox.
    - **P2-2 Confirmation: passes.** Never Active from a redirect alone; activation exactly once across confirm, webhook and reconciliation, including five at the same moment (`CheckoutTests`, `WebhookTests`, `ReconciliationTests`).
    - **P2-3 Webhooks: passes.** Unsigned and tampered rejected; duplicates ignored; out-of-order handled by re-fetching PayPal's state; every stored event visible in the console with Replay (`WebhookTests`).
    - **P2-4 Lifecycle:** every transition of both state diagrams is covered by unit tests and, through simulated PayPal events, by integration tests. **Waiting:** the sandbox run (subscribe, renew, fail with a sandbox card failure, suspend, recover, cancel, change plan), to be recorded here.
    - **P2-5 Enforcement: passes.** Suspended and Expired cannot save or use the kiosk; reports and exports work (`EnforcementTests`).
    - **P2-6 Reconciliation: passes.** A dropped webhook is corrected within one cycle, both the activation in 5 minutes and the suspension in the daily run (`ReconciliationTests`).
    - **P2-7 Behaviour: passes.** Baseline suite 312 of 312 for organizations with full access.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 157, UI 5. No model drift in either context.

- **2026-10-15 — Local database: platform migration M5 applied.** Backup `DB_A65635_inpremdb_before_M5_20261015.bak` taken first and verified; verify before `0|2|0|0|1|0|2|1`, after `1|2|2|2|1|1|0|1`; app data fingerprint unchanged.

- **2026-10-15 — Phase 2 / Step 2.9 — Old billing code removed (C4), its rows moved (migration M5).**
  - **M5** (platform, data only): paid old invoices become offline payments; paying Active/PastDue old subscriptions still on the M1f Free backfill become Invoiced (still Active) with their old period; one note per paying old subscription with its old terms. Re-runnable; down reverses exactly what it added. Rehearsed on LocalDB with synthetic rows.
  - **Removed:** `Services/BillingService.cs`, `Services/BillingBackgroundService.cs`, the `BillingService` registration in `Program.cs`. The organization wizard no longer writes an old `dbo.Subscriptions` row (the platform subscription is the only billing record).
  - **Kept:** `Models/Billing` as legacy table mappings until M6 (see the follow-up); nothing reads or writes them.
  - **Tests:** the wizard test now expects no old billing row; the jobs test checks that the old worker and service types are gone.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 157, UI 5. No model drift in either context.

- **2026-10-15 — Phase 2 / Step 2.8 — In-app Billing page and console subscription actions (C3-1).**
  - **App site, `Areas/Billing`** (the organization's Owner only; other admins get access denied; support sessions are refused as before; never blocked by read-only mode):
    - `/Billing`: plan, price, status, trial end, next payment or access-until, outstanding balance; subscribe (trial or lapsed) or change plan (paying) by choosing a published price (one idempotency key per page view, so a double click is one checkout), straight to PayPal's approve link; payment history; link to cancel.
    - `/Billing/Return?key=…`: the confirm call, then the page polls (2 seconds for 30 seconds, then 10 seconds up to 2 minutes) with the design 7.5 messages, including "taking longer than usual" after 2 minutes. Each poll confirms again (idempotent).
    - `/Billing/Cancelled` (the buyer cancelled at PayPal); `/Billing/Cancel` (own page with a reason; access to the end of the paid period).
    - `/Billing/PayPalReturn`: the plans' fallback page. It finds the checkout by PayPal's `subscription_id` and continues as `/Billing/Return`.
    - Return and cancel addresses are built from the request's host and must be on `PayPal:ReturnUrlAllowList`.
  - **C3-1:** "Billing" link in the back-office sidebar for the organization's Owner only (not during support sessions). The read-only page links owners to Billing.
  - **Console:**
    - The organization's **Subscription** tab shows real data: paid period, cancelled-at-period-end, outstanding balance, PayPal id, free access, people limit, full or read-only.
    - **Actions** (Owners and Billing): **Extend trial** (own page; also turns an expired trial back into a trial), **Free access** until a date or removed (own page, reason required), **Sync with PayPal** (button), **Cancel subscription** (own page, reason).
    - The **Invoices and payments** tab lists payments.
    - The **Billing** menu item lists every subscription, with status and billing filters.
    - `BillingAccountService` audits each change.
  - **Tests:**
    - Integration +4 (`BillingPageTests`): the owner subscribes (to PayPal; back before PayPal confirms = still confirming; the poll sees ACTIVE; change plan offered; fallback page; cancel with a reason); cancel at PayPal; admins have no Billing and a read-only owner can still subscribe; the console actions, each audited, in the Billing list, and refused to Support.
    - The console matrix and the audit coverage list have the new pages and handlers.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 157, UI 5.

- **2026-10-15 — Phase 2 / Step 2.7 — Entitlements and enforcement (C3-2, C3-3, C3-4; P2-5, P2-7).**
  - **`IEntitlementService`** (Application) gives an organization's `AccessStatus`: can write, can use the kiosk, can add people (plan limit or console override), can send SMS (false until Phase 5), plus the read-only reason, trial days left and payment problem.
    - Full access follows `Subscription.HasFullAccess`, so trial expiry is evaluated on every check from the trial end date (lazily).
    - Read once per request and shared by the guard and the banner.
    - An organization with no subscription row keeps full access, as before (test databases; every real database got one in M1f).
  - **`BillingEnforcementMiddleware`**, registered in `Program.cs` after the tenant context; no approved page edited.
    - **Saving refused:** POST, PUT, PATCH and DELETE are refused, except `/api/reports` (reports and exports are POSTs that only read) and what must keep working (sign-in and sign-out, password reset, own account, `/Account` pages including support sessions, `/Billing`, `/api/billing`, `/webhooks`). Pages are redirected to `/Account/ReadOnly`, which explains why; API calls get 403 with a JSON message.
    - **The kiosk pauses:** its pages and its clock and break actions. These are GETs in the approved app, so a method rule alone would have missed them; found while checking the controllers.
    - Organizations with full access pass straight through.
  - **Status banner (C3-2):** the existing `_PlatformBanner` include now also shows read-only (with the reason), a payment problem (PastDue: PayPal is retrying), or the last 14 days of a trial. It renders nothing for active and free organizations.
  - **Tests:**
    - Integration +7 (`EnforcementTests`):
      - **P2-5:** a suspended organization can open every page and run a report and a CSV export, but a page save is sent to the explanation page, an API save gets 403, and the kiosk page and a clock-in are paused with no clocking written; signing out still works.
      - Trial over (before the daily job), expired, and cancelled past its period are read-only.
      - A payment problem and the last trial days show their banner and block nothing; active and free organizations get no banner and an open kiosk; the people limit.
    - **P2-7:** the baseline suite passes unchanged (312).
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 153, UI 5.

- **2026-10-15 — Phase 2 / Step 2.6 — Reconciliation jobs.**
  - **`BillingReconciliation`** (Application), design 7.5 source 3. It uses the same idempotent use cases and the same per-organization lock as confirm and webhooks.
    - **Every 5 minutes** (`billing-checkouts`): every open checkout (Created, AwaitingApproval, Approved; oldest first, up to 500) is confirmed with PayPal.
      - Past its 3 hours → Expired.
      - Approved for more than 24 hours without PayPal saying ACTIVE → Failed, and provider Owners and Billing users are emailed (`CheckoutService.EndStaleAsync`).
    - **Daily at 03:15 UTC** (`billing-daily`): every PayPal subscription not Closed is compared with PayPal and corrected by `SubscriptionSync` (each correction audited as `job:reconciliation`).
      - Trials that have ended are marked Expired (`subscription.trial-expired`). Access already ends at the trial date: `HasFullAccess` reads `TrialEndsUtc` on every check (the lazy evaluation of the step text).
      - The provider gets an email summary when something was corrected or could not be checked.
    - One subscription or checkout failing (PayPal down) does not stop the others; it is tried again next run.
  - **Hangfire recurring jobs** (`BillingReconciliationJob`), registered when the job server starts (App site with `Jobs:RunServer`): the 5-minute run without retries (the next run is 5 minutes away); the daily run with 2 retries; neither runs twice at once.
  - **Local note:** the developer's local app has `Jobs:RunServer=true`, so these jobs run there too. Without PayPal keys they find nothing to do (PayPal "not configured" changes nothing).
  - **Tests:**
    - Integration +5 (`ReconciliationTests`):
      - **P2-6:** a dropped activation webhook is corrected by the next 5-minute run (once); a dropped suspension webhook by the daily run, with the provider summary.
      - stale checkouts expire (3 hours) or fail and alert (24 hours approved); ended trials lose access at once and expire in the daily run; PayPal failing for one checkout does not stop the others.
    - The job-server test now also checks that both recurring jobs are registered with their schedules.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 146, UI 5.

- **2026-10-15 — Phase 2 / Step 2.5 — Webhooks.**
  - **`POST /webhooks/paypal`** (App site only, by the site convention; anonymous; no antiforgery; 256 KB limit; `PayPalWebhookController`):
    - PayPal verifies the signature (`IPaymentProvider.VerifyWebhookAsync`): unverified, not JSON, or without id or type → **400**, logged as suspicious with the transmission id and address (never the body).
    - Stored once by PayPal's event id (`BillingEvent` primary key; a duplicate, even at the same moment, → **200** and nothing new), then queued and answered at once.
  - **`BillingEventProcessor`** (the Hangfire job `BillingEventJob`, 5 automatic retries):
    - **Subscription events re-fetch the subscription from PayPal**, and fail the job if PayPal cannot be reached. A checkout still waiting goes through `CheckoutService.ConfirmAsync` (design 7.5 source 2: activation once).
    - Otherwise **`SubscriptionSync` applies PayPal's current state**: ACTIVE with an outstanding balance and failed payments → PastDue; ACTIVE again → paid (PastDue cleared) or reactivated (from Suspended); SUSPENDED → Suspended; CANCELLED → Canceled (access to the period end); EXPIRED → Expired; plan changes followed. Because it follows the current state, duplicates and out-of-order events end in the same place.
    - `PAYMENT.SALE.COMPLETED` records the payment once per sale id, then syncs (the period moves on). `PAYMENT.SALE.REFUNDED` / `REVERSED` update the payment. Unknown subscriptions and other event types are marked processed with no change.
    - A failure is kept on the event (attempts, last error) and rethrown so Hangfire retries.
  - **One billing lock per organization:** checkout confirm, webhooks and (step 2.6) reconciliation all take `billing:tenant:{id}`. The checkout lock of step 2.4 moved to it, so a webhook sync and a confirm never interleave.
  - **Console "Webhook events"** (Owners and Billing; P2-3): every stored event, filters by status and type, pages of 50. The event's page shows status, attempts, last error and **Replay** (audited as `billing-event.replayed`, queued again). The raw body is not shown: it contains the payer's details.
  - **Found and fixed:** an ACTIVATED event processed while PayPal's API was down was marked processed without applying anything; the processor now re-reads first and fails (retry).
  - **Tests:**
    - Integration +7 (`WebhookTests`): tampered and unsigned rejected and not stored; stored once and queued; duplicate 200; ACTIVATED through the checkout once (also before PayPal says ACTIVE, and with the buyer's return); payment failed, then sale completed, then refunded (payment once); out-of-order SUSPENDED, ACTIVATED after CANCELLED, EXPIRED; unknown and other events; a failure kept, shown in the console without payer details, replayed and finished; Support refused.
    - The console matrix and the audit coverage list have the new page and handler.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 141, UI 5. No model drift.

- **2026-10-15 — Local database: platform migration M1g applied.** Backup `DB_A65635_inpremdb_before_M1g_20261015.bak` taken first and verified; verify `1|0|0`; app data fingerprint unchanged.

- **2026-10-15 — Phase 2 / Step 2.4 — Checkout.**
  - **`CheckoutService`** (Application), shared by the in-app Billing page (step 2.8) and the website (Phase 3):
    - **Start:** convert a trial, subscribe again, or change plan. The browser sends only our price id; the PayPal plan id comes from our catalogue.
      - Refused: return or cancel addresses off the allow-list, prices not offered or not published, and purposes that do not fit the subscription's state.
      - The idempotency key is saved before PayPal is called; the same key returns the same checkout, and the same key for something else is refused.
      - A new subscription carries `custom_id` = the checkout reference and request id `inprem-checkout-{id}`. A plan change uses PayPal's revise.
      - PayPal failing marks the checkout Failed, audited with the debug id.
    - **Confirm** (the one entry point for the confirm call, webhooks and reconciliation): re-reads the subscription from PayPal, then checks it.
      - Checks: `custom_id` (not for plan changes), the subscription id, our catalogue's plan, the currency and amount of any payment, no other organization linked, not expired. A mismatch fails the checkout, is audited, and emails provider Owners and Billing users.
      - APPROVED → checkout Approved, subscription PendingActivation (**never Active from a redirect alone**). ACTIVE → **activate**.
      - A plan change completes only once PayPal shows the new plan (the subscription is ACTIVE all along).
    - **Exactly once (D20):** every confirm runs under a database lock per checkout (`sp_getapplock` in a transaction, so across processes), re-reads after the lock, and `Subscription.Activate` is a no-op the second time. The audit entry `subscription.activated` and the owners' confirmation email happen only when something really changed.
    - **Also:** the buyer's cancel (AwaitingApproval → Cancelled); expiry after 3 hours; cancelling a PayPal subscription (PayPal first, then here; access to the period end; owners emailed). Email failures are audited, never undo a change.
  - **`CheckoutStore`** (Infrastructure), with the lock.
  - **Migration M1g:** the checkout index on the PayPal subscription id is no longer unique. A plan change is a second checkout on the same PayPal subscription; the unique rule that matters stays on `Subscriptions`. Found by the plan-change test. Scripts rehearsed.
  - **Tests:** Integration +12 (`CheckoutTests`, real database, pretend PayPal):
    - start from our catalogue; redirect alone never activates;
    - **five sources at the same moment activate once** (one audit entry, one email); approval then three sources in a row: once;
    - the four mismatches (custom_id, plan, other organization, currency) fail and alert;
    - idempotency; the refusals and PayPal down; expiry; buyer cancel (later ACTIVE ignored);
    - plan change (waits for PayPal's new plan); cancel with access to the period end.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 134, UI 5. No model drift.

- **2026-10-15 — Phase 2 / Step 2.3 — Plan sync ("Publish to PayPal").**
  - **`PlanPublisher`** (Application): a PayPal product for the plan (once) and a PayPal billing plan for every active price that has none.
    - Each PayPal id is saved as soon as PayPal returns it. Request ids come from our own ids (`inprem-product-{plan}`, `inprem-plan-{price}`), so publishing again after a failure finishes the rest without creating anything twice.
    - One audit entry per publish: `plan.published`, or `plan.publish-failed` with what was done and PayPal's debug id.
    - Refused for retired plans, when nothing is left to publish, and when PayPal is not configured.
  - **Price changes:** prices were already fixed once created (one active price per cycle). A new amount means retiring the old price and adding a new one; publishing then creates a new PayPal plan under the same product. Organizations on the old price keep it until they are moved deliberately (plan change, step 2.4).
  - **The fallback is enforced:** the PayPal client refuses to create a plan without `PayPal:PlanReturnUrl` (the developer's "both" decision, step 2.2).
  - **Plan page:** "Publish to PayPal" button (Billing and Owners, only when something is left to publish and PayPal is configured; confirm dialog); the PayPal product and each price's PayPal plan are shown; a note when PayPal is not configured.
  - **Test host:** `FakePaymentProvider` replaces PayPal in every test host. It works in memory, keeps PayPal's request-id behaviour, and can fail the next calls or fail after N calls.
  - **Tests:**
    - Integration +4: `PlanPublishTests` (product and plans, nothing twice, a new price later, a failure halfway then finished, Support refused, the button hidden without PayPal); the plan without the fallback page refused.
    - The audit coverage list has the new handler.
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 122, UI 5.

- **2026-10-15 — Phase 2 / Step 2.2 — PayPal client.**
  - **Verified first** (step text): PayPal's API description marks the create-subscription `application_context` (return and cancel addresses) deprecated, with no replacement. **Developer's decision: both** — the per-subscription addresses, plus our fixed return and cancel pages on every PayPal plan as a fallback. Details: `docs/multitenancy/paypal-notes.md`.
  - **`IPaymentProvider`** (Application): products, plans, subscriptions (create, get, revise, cancel), webhook verification, the return-address allow-list, `PaymentProviderException` (status and PayPal debug id, no payloads).
  - **`PayPalPaymentProvider`** (Infrastructure):
    - PayPalServerSDK **2.4.0** for plans and subscriptions (2.3.0 lacks the subscription status); a typed `HttpClient` for catalog products and webhook verification.
    - Plans: one regular cycle, `auto_bill_outstanding`, `payment_failure_threshold` 1 (D23), `merchant_preferences` fallback.
    - Cancel accepts an already cancelled subscription. A 404 on get is "not known".
  - **One named `HttpClient`:** per-attempt and total timeouts; 3 retries of transient failures (creates carry `PayPal-Request-Id`, so a retry never makes two); a cached OAuth token; a logging handler with PayPal's debug id and no headers or bodies.
  - **Found and fixed:** the framework's HttpClient logging wrote the app's Basic credentials at Trace level; it is removed for this client.
  - **`PayPalOptions`**, validated at startup: sandbox/live (live refused without `AllowLive`), credentials together, https origins, plan pages on an allowed origin, timeout range. Without credentials the app runs with PayPal "not configured" (tests, and development until the keys are set).
  - Registered in `Program.cs` (`AddPayPal`).
  - **Tests:**
    - Integration +19 (`PayPalClientTests`, a pretend PayPal): what is sent (custom_id, addresses, request id, D23 rules, fallback pages); how answers are read; the allow-list; retries with the same request id, and no retry on 4xx; the debug id; tokens cached; webhook verification (success, failure, missing header, bad JSON, no webhook id, PayPal down); not configured; startup checks; no payer data, token or credentials in the logs.
    - Unit +1 (the SDK's status spelling).
  - **Results:** Unit 161, Architecture 11, Baseline 312, Integration 118, UI 5. No model drift.

- **2026-10-15 — Phase 1 merged; Phase 2 started.** `mt/phase1` fast-forwarded into `mt/integration` (`87ab80d`) and pushed, at the developer's request (P1-1 walkthrough and P1-3 staging timing still open). Branch `mt/phase2` from there.

- **2026-10-15 — Local database: platform migration M1f applied.** Backup `DB_A65635_inpremdb_before_M1f_20261015.bak` taken first and verified; verify `1|3|2|0|4` (four organizations now on Free); app data fingerprint unchanged.

- **2026-10-15 — Phase 2 / Step 2.1 — Billing domain and data (platform migration M1f).**
  - **`Subscription`** gets the PayPal fields (paid period, cancel at period end, outstanding balance, PayPal subscription id, status date) and the console overrides (free access until a date, people limit).
    - Its methods follow the design 8 state diagram: trial expiry, awaiting activation, activation, past due, payment, suspension, cancellation, PayPal expiry, closing after the 90-day retention (D24), plan change, refresh.
    - Moves the diagram does not have are refused. Repeating a move that already happened returns false and changes nothing, so confirm, webhook and reconciliation can all report the same activation (D20: exactly once).
    - `HasFullAccess` decides saving and kiosk (step 2.7): Free, a running trial, PendingActivation, Active, PastDue, Canceled until the period end, or a free-access override. Expiry of a trial takes effect at once, before the daily job.
    - Two transitions added beyond the diagram, both needed by the events of step 2.5: PayPal's `EXPIRED` from Active, PastDue, Canceled or Suspended; and resubscribing from Canceled or Suspended.
  - **`CheckoutSession`** (design 7.5 states): random `chk_` reference for PayPal's `custom_id`, 3-hour life, https return and cancel addresses, idempotency key (unique), one organization or one signup. Final states never change. PayPal's status strings are mapped by `PayPalStatus.Parse`.
  - **`Payment`** (PayPal sale or offline; refund and reversal) and **`BillingEvent`** (PayPal event id as the key, raw body for replay and never logged, attempts and last error).
  - **Plan links:** `Plan.PayPalProductId` and `PlanPrice.PayPalPlanId`, each set once (for step 2.3).
  - **Migration M1f:** additive; foreign keys to `dbo.Tenants` by SQL as before; existing subscriptions get their status date; **every organization without a subscription gets a Free one** (D26; see the follow-up). Scripts `platform-M1f-up/down/verify` rehearsed on LocalDB.
  - **Tests:** Unit +24 (`SubscriptionLifecycleTests`: every transition of both diagrams, refusals, repeats, access, overrides, checkout input checks, status mapping, payments, events, links).
  - **Results:** Unit 160, Architecture 11, Baseline 312, Integration 99, UI 5. No model drift.

- **2026-10-15 — Phase 1 bundle and exit review.**
  - **Bundles:** `scripts/migrations/phase1-up.sql` and `phase1-down.sql`, generated by `build-phase1-bundle.ps1` from the reviewed M1c, M1d and M1e scripts, with the Phase 0 guards. They also refuse a database without the complete Phase 0 schema. CI checks both bundles are up to date.
    - **Rehearsed** on LocalDB with synthetic data: refused without Phase 0; up, up, down, down, up all as expected; verify correct each time; an unknown migration refused; then back to the baseline with `phase1-down` and `phase0-down`. Details: `scripts/migrations/README.md`.
    - **Local database:** `phase1-up.sql` ran as a no-op (already had M1c to M1e); guards passed, verify `M1c=1|2|1|0|0 M1d=1|3|1|0|0 M1e=1|1|1|0`, app data unchanged.
  - **New test `ConsoleAuditCoverageTests`:** every console POST handler (32) is listed with the audit entry it writes and the test that checks it. A new handler fails the test until it is reviewed. The plan pages test now also covers "offer again" (`plan.reactivated`), the only write action that had no test.
  - **Exit checks:**
    - **P1-1 Page pattern: automated part passes** (`Console_List_Pages_Have_No_Forms_Except_Sign_Out`). **Waiting:** the developer's walkthrough, to be recorded here.
    - **P1-2 Provider security: passes.** Console unreachable without two-factor (setup journey, role matrix, `Console_Every_Signed_In_Page_Is_In_The_Matrix`); matrix for Support, Billing and Owner, with session limits and fresh-code pages.
    - **P1-3 Onboarding: automated part passes** (`Organization_Wizard_Creates_Everything_And_The_Owner_Signs_In`, invitation for existing accounts, failure leaves nothing). **Waiting:** "under 3 minutes on staging" needs the test site (deployment after all phases); it can be timed locally meanwhile.
    - **P1-4 Audit: passes.** 100% of console write actions audited and tested (coverage test above; the org detail test checks exactly one entry per action).
    - **P1-5 Support sessions: passes.** Expiry, banner, billing blocked, audit per request, single-use hand-off (`SupportSessionTests`, unit rules).
    - **P1-6 Isolation: passes.** The tenant-isolation and host-isolation suites read the endpoint table, so they cover every new route automatically; 0 failures. Baseline suite changed only where C4 code was removed (step 1.7, tagged `ChangedBy`).
    - **P1-7 Admin site protection: passes** (`AdminAllowListTests`).
  - **Results:** Unit 136, Architecture 11, Baseline 312, Integration 99, UI 5. No model drift in either context.

- **2026-10-14 — Phase 1 / Step 1.7 — Removed replaced code (C4).** Its own commit.
  - **Removed:** `Pages/Platform/**` (Tenants, TenantDetail, Invoices), `Controllers/Api/InvoicesController.cs`, `Services/TenantAdminService.cs`, and in `Program.cs` its registration, the `SuperAdminOnly` policy and the `/Platform` folder rule. They stay in git history.
  - **Kept** until Phase 2: `BillingService`, `BillingBackgroundService`, `Models/Billing`, their tables and migrations. The `SuperAdmin` role stays, so existing accounts and role rows remain valid.
  - **Login (App site):** a SuperAdmin account without any active organization membership is signed out again, with the message that platform operations moved to the Provider Console. Before, it was sent to `/Platform/Tenants`. The local database has one such account, which now gets this message.
  - **Support sessions:** the blocked billing paths are now `/Billing` and `/api/billing` (the removed paths are gone; they answer 404).
  - **Tests:**
    - Baseline: the three SuperAdmin access tests became `Removed_Platform_Pages_Are_Gone` (404 for anonymous and Admin), tagged `ChangedBy`.
    - Integration: the platform-operator sign-in test now expects the message; the support session billing test uses `/Billing/Subscription`.
  - **Results:** Unit 136, Architecture 11, Baseline 312 (−12 old, +4 new), Integration 98, UI 5.

- **2026-10-14 — Local database: platform migration M1e applied** (developer's request). Backup `DB_A65635_inpremdb_before_M1e_20261014.bak` was taken first and verified; verify gave `1|1|1|0`; the app data fingerprint is unchanged.

- **2026-10-14 — Phase 1 / Step 1.6 — Audit log and dashboard shell.**
  - **`AuditLogService`** (Application) over **`IAuditReader`** (Infrastructure).
    - **Audit log** (Owners): filters by provider user, organization, action (prefix, with suggestions) and date range (whole UTC days); pages of 50; newest first; actors shown by email.
    - **CSV export** of the filtered log: UTF-8 with BOM, at most 50,000 rows, standard CSV quoting. Cells that a spreadsheet would run as a formula (starting `= + - @`, tab or CR) are prefixed with an apostrophe.
  - **Dashboard** (every provider user):
    - organizations by status (links to the filtered list);
    - subscriptions by status, and trials ending within 14 days (real data from platform subscriptions);
    - failed Hangfire jobs (shows "—" if the storage cannot be read);
    - the owners figure;
    - the latest 10 audit entries.
  - **Fix: each app now has its own Hangfire storage** (`AppJobStorage`, registered for DI and for Hangfire's configuration). Hangfire's default registration hands out the process-wide `JobStorage.Current` as it is when first asked. When several apps share a process (the integration tests) that can be another app's storage on a dropped database, which gave an occasional 500 on the jobs dashboard in the role matrix. Not reproduced in two full runs since; production is a single app per process.
  - **Tests:**
    - Unit +11: CSV cells, quoting and formula prefixes.
    - Integration +2: audit filters, paging and a CSV with a neutralised formula; dashboard counts update on suspension, a 7-day trial is listed, the failed-jobs tile.
  - **Results:** Unit 136, Architecture 11, Baseline 320, Integration 98 (×3 runs), UI 5.

- **2026-10-14 — Phase 1 / Step 1.5 — Support sessions.**
  - **`SupportSession`** (platform migration **M1e**): organization, provider user, reason (required, shown to the organization), length 15 / 30 / 60 (default) / 120 / 240 minutes, ending.
    - The hand-off token is stored only as a hash. It works once, within 60 seconds, while the session is active.
  - **Starting:** from the organization's page, Owners and Support only (new policy `ProviderSupportSessions`), with a fresh two-factor code. The console sends the browser straight to the App site's hand-off.
    - **Hand-off page:** it only shows what will happen, so link scanners cannot use it. "Continue" signs in as admin of that organization: the provider's own account plus session, organization and Admin-role claims.
    - Identity's periodic refresh keeps those claims.
  - **On every App request during a session** (`TenantContextMiddleware`):
    - the session is checked again (not ended, not past its time, and the provider user still active and allowed); otherwise the person is signed out and sent to "Support session ended";
    - the organization context comes from the session, and the request is audited (method and path; never bodies);
    - billing paths are refused with 403 and audited as blocked: the Billing area, `/Platform`, `/api/invoices`, `/api/billing`.
  - **Banner (C3-2):** `Pages/Shared/_PlatformBanner.cshtml`, included in `_MainLayout` and also in `_Layout`, the second back-office layout (an extension of C3-2, so that every page shows it). It says who is working until when, with an "End session" button.
    - Ending is possible from the banner, or from the console ("End now" on the organization's page, Owners and Support).
  - **Organization history:** `/Account/SupportSessions` for the organization's admins (who, when, why, ended). The provider side sees the sessions in the organization's Audit tab.
  - **Tests:**
    - Unit +3: lengths, reasons, single-use 60-second hand-off, ending.
    - Integration +4: admin of Alpha only, with the banner; billing 403; audit per request; single use; 60-second expiry; ending by time, from the console and on disable; Billing users cannot start; Alpha sees its history and Beta does not.
    - The role matrix covers the StartSupport page.
  - **Results:** Unit 125, Architecture 11, Baseline 320, Integration 96, UI 5. No model drift.

- **2026-10-14 — Local database: platform migrations M1c and M1d applied** (developer's request). Backup `DB_A65635_inpremdb_before_phase1_20261014.bak` was taken first and verified. Verify gave M1c `1|2|1|0|0` and M1d `1|3|1|0|0`; the app data fingerprint is unchanged.

- **2026-10-14 — Phase 1 / Step 1.4 — Provider users and peer reset.**
  - **`ProviderSecurityService`:** list, invite (any role), change role, disable (with reason), new setup link. Only active Owners act, and never on themselves.
    - Invitations reuse the bootstrap mechanism: an account without a password, status Invited, a 24-hour setup link, then two-factor.
    - An email that already has an account (e.g. an organization admin) is refused: provider accounts are separate.
    - Disabling withdraws pending links, locks the account and ends every session.
    - Invite, role change and disable are audited and announced to all owners and the security mailbox.
    - A "last active owner" check stays as a safety net. It cannot trigger today, because the acting owner is always a remaining owner.
  - **Pages** (Owners only):
    - list, and a read-only page per person (actions hidden for yourself);
    - separate Invite, Role, Disable and ResetTwoFactor pages; "Send a new setup link" for people who have not set up yet.
    - Invite, Role and Disable need a code from the last 5 minutes. **ResetTwoFactor** is the Level 2 screen: a reason plus your own current code, built on the Phase 0 use case.
  - **Fix:** the jobs dashboard now uses the app's own Hangfire storage instead of the process-wide `JobStorage.Current`. That global made a matrix test fail when test hosts ran in parallel; a single production host was not affected.
  - **Tests:**
    - Unit +6: invite, refusals, roles, disable, resend.
    - Integration +3: invite → setup → role change with re-authentication → Level 2 reset (session ends at once) → disable (sign-in refused), audit and notifications; a second owner removes the go-live warning (health Healthy); resend replaces the link.
    - The role matrix covers the 5 new pages.
  - **Results:** Unit 122, Architecture 11, Baseline 320, Integration 92, UI 5.
  - **UI flake to watch:** in one run the browser (UI) suite had 1 failure, at about twice its usual duration (56 s instead of 24 s). The output was not captured, so the failing test is unknown. Four reruns all passed. If it happens again, keep the log; a longer wait in that test is the likely fix.

- **2026-10-14 — Phase 1 / Step 1.2 — Organizations.**
  - **Domain:** `TenantProfile` (unique slug: 3–50 characters, lower-case, reserved names refused; status Active/Suspended with reason), `TenantNote`, `Subscription`.
    - Design 8 states. Phase 1 starts PayPal as Trialing (at least 1 day, since paying starts in Phase 2), Invoiced as Trialing or Active, Free as Active.
    - `TenantMembership.LastSignInUtc` is recorded at every admin sign-in (claims factory and "Choose organization").
  - **`OrganizationService`:** list (search name / slug / owner email; filters status, plan, created dates; pages of 25), per-step wizard checks, create, edit, suspend / reactivate (reason required), notes, resend invitation, password reset, transfer ownership. Every action writes exactly one audit entry carrying the organization's id.
  - **Creation is one transaction across both schemas** (app context plus a platform context on the same connection):
    - creates the organization, the old app's billing row (its C4 pages still expect it), the owner account (new: no password, Admin role), the Owner membership (Active for a new account, Invited for an existing one), the profile, the subscription and the audit entry;
    - a failure leaves nothing (tested with a database trigger) and the page says "nothing was saved";
    - the email goes after the commit. A new owner gets a "choose your password" link (Identity reset link to the approved reset page, 24 hours); an existing account gets an invitation to accept. If sending fails, the organization stays, with a warning and "Resend invitation".
  - **Pages:**
    - list with filters and pagination;
    - 4-step wizard with a live address-name check (`wwwroot/provider/console.js`);
    - detail with 8 tabs (Overview, Subscription, Invoices placeholder, Admins with last sign-in, two-factor and actions, Kiosk placeholder, Messages placeholder, Audit, Notes);
    - separate Edit, Suspend, Reactivate, Add note and Transfer ownership pages.
  - **Permissions:** viewing, notes, resend and reset are for every provider user; create, edit, suspend, reactivate and transfer are for Owners. Transfer needs a code from the last 5 minutes (`[RequireRecentTwoFactor]`, now tested end to end).
  - **Suspension** is recorded with its reason; read-only enforcement comes with billing in Phase 2 (C3-3), as designed, and the page says so.
  - **Migration M1d:** profiles, notes and subscriptions, plus the membership column. It backfills a profile for every organization (`org-{Id}`; Suspended where `IsActive` = 0). Organizations created later by older paths get one on first change. Rehearsed.
  - **The console reads the app's tables past their per-organization filters on purpose**, including `Tenants`, whose filter hid new organizations at first.
  - **Tests:**
    - Unit +17: slugs, status, notes, subscription starts.
    - Integration +5: the wizard end to end (owner sets a password from the email and reaches the back office: P1-3), existing-account owner invited, atomic failure, every detail action with one audit entry each, list search / filter / paging.
    - The role matrix covers all 7 new pages.
    - The M2 backfill test reads only M2's columns.
  - **Results:** Unit 116, Architecture 11, Baseline 320, Integration 89, UI 5. No model drift; no new advisories.
  - **Deployment note:** the step 1.2 build needs platform migrations M1c and M1d applied first (local databases too).

- **2026-10-14 — Phase 1 / Step 1.3 — Plans (local only), done before 1.2.** The organization wizard (1.2) needs plans to choose from, so the order was swapped.
  - **Domain** `Plan` and `PlanPrice`, platform tables via migration **M1c**:
    - plan: code (lower-case, unique, fixed), name, description, features, people limit, trial days (default 30, D21), shown on the pricing page or not, Active / Retired;
    - price: cycle Monthly / Yearly, amount (max 2 decimals), USD (D25), `PayPalPlanId` empty until Phase 2;
    - prices are fixed once created, with one active price per cycle; a new amount is a new price; retiring a plan retires its prices.
  - **`PlanService`:** create (with first price), edit, add / retire price, retire / offer again. Each change saves exactly one audit entry together with it; refusals change nothing and write none. `ListOfferedAsync` lists active plans with an active price, for the wizard.
  - **Pages** (UI rules): list with search and status filter and "+ New plan"; New, Edit and Add-price pages; read-only detail with Retire / Offer again / Retire price buttons, confirmed in a dialog.
    - **Permissions:** viewing needs Support; changes need Billing (pages and handlers both check).
  - **Scripts:** `platform-M1c-up/down/verify` rehearsed (up `1|2|1|0|0`, idempotent, down clean). Not in the Phase 0 bundle; Phase 1 gets its own at the end.
  - **Tests:**
    - Unit +12: rules, service, audit per change, refusals.
    - Integration +3: the pages end to end; Support can view but not change.
    - The role matrix now covers the plan sub-pages.
    - The form helper no longer submits the layout's sign-out form by accident (`SignOutAsync` added).
  - **Results:** Unit 99, Architecture 11, Baseline 320, Integration 84, UI 5. No model drift.

- **2026-10-14 — Phase 0 merged; Phase 1 started.** `mt/phase0` was fast-forwarded into `mt/integration` (local; not pushed), and `mt/phase1` branched from it.

- **2026-10-14 — Phase 1 / Step 1.1 — Area, layout, authentication.**
  - **Console layout** (`Areas/Provider/Pages/Shared/_ConsoleLayout.cshtml`, styles in `wwwroot/provider/console.css`; approved CSS untouched).
    - The menu has 10 items, each shown only to roles allowed to open it.
    - Screens built later are short placeholder pages that say when they arrive.
    - The "fewer than two owners" banner is on every page.
    - Routes sit at the Admin site's root (`/`, `/Tenants`, `/Plans`, …; `/Provider/...` with `SiteMode=All`).
  - **Roles:** policies `ProviderSupport`, `ProviderBilling` and `ProviderOwner` (roles nest: Owner ⊇ Billing ⊇ Support). Every Provider page needs at least Support, unless it is `[AllowAnonymous]`.
    - **Default permissions** (design 5.5 has no matrix, so these are recorded here; change if needed): Support opens Dashboard, Organizations and Plans; Billing adds Billing and Webhook events; Owner adds Notifications, Jobs, Audit log, Provider users and Settings.
    - Without a sign-in, you go to the provider login; with the wrong role, to the "No access" page (403).
  - **Sessions:** every provider sign-in goes through `ProviderSession` (start, last-seen and two-factor times in the cookie).
    - The session ends after 30 idle minutes, or 8 hours at most; a re-sign-in keeps the start time.
    - Identity's periodic refresh keeps these times.
  - **Re-authentication:** `[RequireRecentTwoFactor]` on a page makes its POST handlers need a code from the last 5 minutes. The `Reauthenticate` page then returns to local addresses only.
  - **IP allow-list (D17):** `AdminSite:AllowedNetworks` (CIDR or single addresses, validated at startup). Outside addresses get 403 on an Admin site, and on console requests of a combined site. It is off when the list is empty and in Development.
  - **C3-1:** the old SuperAdmin "Tenants" and "Invoices" links were removed from `Pages/Shared/_Sidebar.cshtml`. The `/Platform` pages themselves stay until step 1.7.
  - **Tests (Integration +12):**
    - role matrix: every signed-in console page × anonymous, organization admin, Support, Billing and Owner, against an independently written permission table, plus a check that every console page is in the matrix;
    - menu per role; list pages have no POST forms except sign-out;
    - re-authentication; idle and 8-hour limits;
    - allow-list (Admin site, combined site, empty list, App site, invalid entry); sidebar.
    - Mutations (longer idle time; Support allowed into Billing) were caught.
  - **Also:** the Provider fixture installs the Hangfire tables (shared `HangfireSchema` helper). The tenant-admin journey now expects "No access".
  - **Results:** Unit 87, Architecture 11, Baseline 320, Integration 82, UI 5.
  - **Follow-up:** `[RequireRecentTwoFactor]` gets its end-to-end test with the first sensitive action (inviting provider users, step 1.4).

_(date — phase/step — summary — tests — next)_

- **2026-10-14 — Hardcoded SMTP credential removed (P0-5; the developer's decision: old trial settings, take it off).**
  - **Change:** `EmailService` (C1, changed at the developer's request) now reads `Email:SmtpHost`, `SmtpPort` (default 587), `EnableSsl`, `UserName`, `Password` and `From` from configuration.
    - Without settings, it fails with "Email is not configured".
    - Port 465 is refused with an explanation (`SmtpClient` cannot do implicit TLS, which likely explains why the old settings timed out).
    - In Development, `Email:DropFolder` saves every email, including the approved "Forgot password" page's, as a file.
  - **Registration:** the platform sender always goes through `EmailService`.
  - **Tests:** Integration +4 (no settings, port 465, drop folder in Development only, no credentials in the source). All suites pass: Unit 87, Architecture 11, Baseline 320, Integration 70, UI 5.
  - **Still to do:**
    - **Exposed in history:** the credential stays in public Git history (commit 10e1e22). Treat it as exposed: the account owner should change or close it.
    - **Gitleaks:** the `.gitleaksignore` line now covers only that historical commit.
    - **Repository visibility:** making the repository private needs the Spalce account's administrator; the developer has no access.
    - **Hosts:** set `Email__*` on every host (`deployment-phase0.md`).

- **2026-10-14 — Package upgrade (developer's request: "fix the upgrade but make sure nothing breaks").**
  - **Upgraded:** every Microsoft package moved from 8.0.0 to its latest 8.0.x security patch: EF Core, Identity, data protection and test hosting to 8.0.32; the scaffolding tool to 8.0.23. Same major version, no feature changes. Also, `dotnet-ef` local tool 7.0.2 → 8.0.32.
  - **Advisories:** the 9 tolerated high/critical advisories are gone (SqlClient, Azure.Identity, Caching.Memory, System.Text.Json ×2, NuGet ×3, Asn1), and `known-vulnerable-packages.txt` is now empty. One low-severity advisory remains, in NuGet 6.11 of the development-only scaffolding package.
  - **Checks:**
    - no model drift in either context;
    - Unit 87, Architecture 11, Baseline 320, Integration 66, UI 5, all passing;
    - full rehearsal (upgrade, app smoke test, R2, R3) green with the upgraded build.
  - The rehearsal now builds with `--configuration Release`.

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
