# Handover: finishing the multi-tenancy work without Claude

Written 2026-10-20, when Phases 0 to 6 and the cutover preparation were done and merged into `mt/integration` (commit `c88c5dc`, local, not pushed). It tells you exactly what to do when the PayPal and Twilio details arrive, how to test, how to deploy, what can go wrong, and what is left after that.

**The short version**
- **PayPal and Twilio need no new code and no new tables.** Both are fully built and tested against a pretend PayPal and a pretend Twilio. With the real details you only change **settings**, set up a few things in the PayPal and Twilio dashboards, test, and deploy.
- **Database:** the schema for everything built so far is in the bundles `scripts/migrations/phase0-up.sql` … `phase6-up.sql`. Nothing more is needed for PayPal or Twilio.
- **Still to build (code):** Phase 7, hardening (section 9). It is optional for a first release on the test site, needed before production.

---

## Contents

1. [What to ask for](#1-what-to-ask-for)
2. [Where settings go](#2-where-settings-go)
3. [PayPal (sandbox)](#3-paypal-sandbox)
4. [Twilio (test account)](#4-twilio-test-account)
5. [Testing after the changes](#5-testing-after-the-changes)
6. [Database and migrations](#6-database-and-migrations)
7. [Deployment plan](#7-deployment-plan)
8. [Going live (real money, real texts)](#8-going-live-real-money-real-texts)
9. [What is left to build: Phase 7](#9-what-is-left-to-build-phase-7)
10. [Possible errors and how to fix them](#10-possible-errors-and-how-to-fix-them)
11. [Other things to know](#11-other-things-to-know)

---

## 1. What to ask for

From the **project owner** (Inprem's PayPal Business account and Twilio account; you can't open PayPal from Ghana):

| Item | What it looks like | Where it goes |
|---|---|---|
| PayPal sandbox **Client ID** | about 80 letters/digits | `PayPal:ClientId` |
| PayPal sandbox **Secret** | about 80 letters/digits | `PayPal:Secret` |
| PayPal sandbox **Webhook ID** (after section 3.2) | e.g. `8PT597110X687430LKGECATA` | `PayPal:WebhookId` |
| A PayPal sandbox **buyer** account (email + password) | made in the sandbox dashboard | for paying in tests |
| Twilio **Account SID** | `AC` + 32 characters | `Twilio:AccountSid` |
| Twilio **Auth Token** | 32 characters | `Twilio:AuthToken` |
| Twilio **API key** (optional; replaces the Auth Token for sending) | `SK` + 32 characters, and a secret | `Twilio:ApiKeySid`, `Twilio:ApiKeySecret` |
| Twilio **phone number**, or a **Messaging Service SID** with the number in it | `+1…`, or `MG` + 32 characters | `Twilio:FromNumber`, or `Twilio:MessagingServiceSid` |
| Twilio **Verify Service SID** (optional) | `VA` + 32 characters | `Twilio:VerifyServiceSid`; without it the app makes the sign-in codes itself |
| The **sender address** for email (Twilio Email API) | e.g. `no-reply@inpremclockin.org` | `Email:From` |
| Their answers on: approval of the staff texts box (C3-8), sender registration, the 21:00 default (section 11) | written | `docs/multitenancy/build/PROGRESS.md`, "Open follow-ups" |

Ask for them to be shared **privately** (not by email in plain text if avoidable). They must never be committed to the repository: CI's secret scan fails the build if they are.

---

## 2. Where settings go

**On your computer (local copy):** user-secrets. Run from the repository folder:

```powershell
dotnet user-secrets set "PayPal:ClientId" "<client id>" --project InpremClockingApp
```

The values are stored outside the repository, in `%APPDATA%\Microsoft\UserSecrets\`. List them (with values!) with `dotnet user-secrets list --project InpremClockingApp`. Remove one with `dotnet user-secrets remove "<key>" --project InpremClockingApp`.

**On a server (SmarterASP test site, later the App and Admin sites):** the host's settings, as environment variables, or a server-only `appsettings.Production.json` that you keep on the server and never commit.
- In environment variables `:` becomes `__` (two underscores), and list items get `__0`, `__1`: `PayPal__ClientId`, `PayPal__ReturnUrlAllowList__0`.
- If you upload with Web Deploy, don't tick "remove additional files at destination", or the server's `appsettings.Production.json` is deleted.

`docs/multitenancy/runbooks/site-configuration.md` lists every setting of every site.

**After changing settings, restart the app.** Settings are read at start-up. A wrong setting stops the app at start-up with a message naming it (section 10).

---

## 3. PayPal (sandbox)

### 3.1 What the code already does

All in place, nothing to change:
- subscriptions: plans published to PayPal, checkout, return page, cancel, plan change;
- webhooks (`POST /webhooks/paypal`), verified with PayPal before use;
- a reconciliation job every 5 minutes for open checkouts, and a daily check at 03:15 UTC;
- PayPal invoices, refunds, and the monthly reconciliation report.

The details are in `docs/multitenancy/paypal-notes.md`. The code is under `src/Inprem.Platform.Infrastructure/PayPal/`.

### 3.2 In the PayPal developer dashboard (project owner, or you with their login)

1. **developer.paypal.com** → **Apps & Credentials** → **Sandbox** → the app (or **Create App**, type Merchant). Copy **Client ID** and **Secret**.
2. In the app's settings, make sure these features are enabled (names may vary slightly in PayPal's dashboard):
   - **Subscriptions** (or "Accept payments");
   - **Invoicing**;
   - **Transaction search**: needed by the monthly report. It can take some hours after switching on.
3. **Webhooks** (on the same app) → **Add Webhook**:
   - **URL:** the address PayPal can reach + `/webhooks/paypal`. For the test site: `https://<test-site-address>/webhooks/paypal`. Later: `https://app.inpremclockin.org/webhooks/paypal`. A local copy needs a tunnel (section 3.4).
   - **Events:** tick these:
     - `BILLING.SUBSCRIPTION.ACTIVATED`, `BILLING.SUBSCRIPTION.UPDATED`, `BILLING.SUBSCRIPTION.RE-ACTIVATED`, `BILLING.SUBSCRIPTION.SUSPENDED`, `BILLING.SUBSCRIPTION.CANCELLED`, `BILLING.SUBSCRIPTION.EXPIRED`, `BILLING.SUBSCRIPTION.PAYMENT.FAILED`;
     - `PAYMENT.SALE.COMPLETED`, `PAYMENT.SALE.REFUNDED`, `PAYMENT.SALE.REVERSED`;
     - `INVOICING.INVOICE.CREATED`, `INVOICING.INVOICE.UPDATED`, `INVOICING.INVOICE.SCHEDULED`, `INVOICING.INVOICE.PAID`, `INVOICING.INVOICE.CANCELLED`, `INVOICING.INVOICE.REFUNDED`;
     - `PAYMENT.CAPTURE.REFUNDED`, `PAYMENT.CAPTURE.REVERSED`.
   - (Simpler: "All events". Events the app does not use are stored and marked processed, which is harmless.)
   - Save, then copy the webhook's **ID**. **Each webhook URL has its own ID**: a local tunnel, the test site and production each need their own webhook and their own `PayPal:WebhookId`.
4. **Sandbox accounts** (**Testing Tools** → **Sandbox Accounts**): one **Personal** (buyer) account to pay with. Note its email and password.

### 3.3 Settings

Local copy (`https://localhost:7078`), user-secrets:

```powershell
dotnet user-secrets set "PayPal:Mode" "Sandbox" --project InpremClockingApp
dotnet user-secrets set "PayPal:ClientId" "<sandbox client id>" --project InpremClockingApp
dotnet user-secrets set "PayPal:Secret" "<sandbox secret>" --project InpremClockingApp
dotnet user-secrets set "PayPal:WebhookId" "<webhook id for the tunnel URL>" --project InpremClockingApp
dotnet user-secrets set "PayPal:ReturnUrlAllowList:0" "https://localhost:7078" --project InpremClockingApp
dotnet user-secrets set "PayPal:PlanReturnUrl" "https://localhost:7078/Billing/PayPalReturn" --project InpremClockingApp
dotnet user-secrets set "PayPal:PlanCancelUrl" "https://localhost:7078/Billing" --project InpremClockingApp
```

On the test site, the same as host settings with `__`, using the test site's address:

| Setting | Value |
|---|---|
| `PayPal__Mode` | `Sandbox` |
| `PayPal__ClientId`, `PayPal__Secret` | the sandbox keys |
| `PayPal__WebhookId` | the ID of the webhook whose URL is the test site's |
| `PayPal__ReturnUrlAllowList__0` | `https://<test-site-address>` (no path, no trailing slash) |
| `PayPal__PlanReturnUrl` | `https://<test-site-address>/Billing/PayPalReturn` |
| `PayPal__PlanCancelUrl` | `https://<test-site-address>/Billing` |

When the web team's site exists, add its origins too: `PayPal__ReturnUrlAllowList__1` = `https://inpremclockin.org` and `__2` = `https://www.inpremclockin.org`. Website checkouts send buyers back there.

**Do not set** `PayPal:Mode` = `Live` or `PayPal:AllowLive` now. Live is section 8.

### 3.4 Webhooks on your local copy (a tunnel)

PayPal must reach the app over the internet; `localhost` is not reachable. Two choices:
- **Test on the SmarterASP test site** (simplest, recommended): it already has a public https address.
- **Or a tunnel** to your computer, e.g. ngrok:
  ```powershell
  ngrok http https://localhost:7078
  ```
  ngrok prints an address like `https://ab12-34-56.ngrok-free.app`.
  - Use `https://ab12-34-56.ngrok-free.app/webhooks/paypal` as the webhook URL in 3.2.
  - Keep browsing the app at `https://localhost:7078`.
  - Free ngrok addresses change every time: update the webhook URL (and its `PayPal:WebhookId`) each time.

Without webhooks, things still work but slower:
- the checkout's return page asks PayPal directly;
- the 5-minute job corrects open checkouts;
- the daily check corrects the rest.

Webhooks make it immediate.

### 3.5 First run with PayPal

1. Restart the app. Sign in to the console.
2. **Plans** → a plan with **Show on the public pricing page** ticked → **Publish to PayPal**.
   - **Expected:** each price shows an id in **PayPal plan** (like `P-…`). The plan's PayPal product is created automatically.
3. Then run the PayPal tests (section 5).

---

## 4. Twilio (test account)

### 4.1 What the code already does

All in place, nothing to change:
- texts through a **Messaging Service**, or without one from a plain Twilio number (`Twilio:FromNumber`), with delivery reports to `POST /webhooks/twilio/status`;
- STOP, START and HELP replies through `POST /webhooks/twilio/inbound`, both checked with Twilio's signature;
- sign-in codes through **Twilio Verify**, or without it made by the app and texted like any other text;
- emails through Twilio's Email API when `Email:Provider` = `Twilio` (`InpremClockingApp/Services/EmailService.cs`);
- sign-in to Twilio with an API key when `Twilio:ApiKeySid` and `Twilio:ApiKeySecret` are set, otherwise with the Account SID and Auth Token;
- consent, quiet hours, monthly and daily limits.

Code: `src/Inprem.Platform.Infrastructure/Twilio/`, `InpremClockingApp/Notifications/TwilioWebhookController.cs`.

**One setting switches everything:** with `Twilio:Mode` = `Twilio`, both texts and sign-in codes go through Twilio. With `StandIn` (the default), nothing leaves the computer.

### 4.2 In the Twilio console (project owner, or you with their login)

1. **Account SID** and **Auth Token**: on the console's home page (**Account Info**).
2. **A phone number**: **Phone Numbers** → **Buy a number** (one with SMS). On a **trial** account, Twilio gives one number free, but:
   - it texts **only verified numbers** (add yours under **Verified Caller IDs**);
   - every text starts with "Sent from your Twilio trial account".
3. **Messaging Service** (optional: without one, set `Twilio:FromNumber` to the number, and enter the incoming-messages address on the number itself: **Phone Numbers** → the number → **Messaging** → **A message comes in**, `https://<public address>/webhooks/twilio/inbound`, **HTTP POST**): **Messaging** → **Services** → **Create Messaging Service**.
   - **Sender Pool:** add the number.
   - **Integration** → **Incoming Messages**: choose "Send a webhook". Request URL: `https://<public address>/webhooks/twilio/inbound`, method **HTTP POST**.
   - Leave the **Status callback URL** empty: the app gives it with each text (`<Twilio:WebhookBaseUrl>/webhooks/twilio/status`).
   - **Opt-out management:** leave Twilio's default STOP handling on. Twilio blocks texts to a number that replied STOP (error 21610). The app also records the STOP, so the log and the **Opt-outs** page are right.
   - Copy the Service SID (`MG…`).
4. **Verify service** (optional: without one the app makes the codes itself): **Verify** → **Services** → **Create**. Friendly name e.g. "Inprem Clock-in", code length 6. Copy its SID (`VA…`). Verify sends from Twilio's own numbers; it needs no registration.
5. **Sender registration** (before texting **US** numbers for real):
   - either **toll-free verification** (a toll-free number);
   - or **A2P 10DLC** (brand + campaign) for a normal US number.
   - Without it, US carriers block the texts (error 30032 or 30034).
   - The use case is "account notifications and reminders (no marketing)"; the consent wording the app shows is in `src/Inprem.Platform.Application/Notifications/NotificationTemplates.cs` (`ConsentWording`).
   - Record Twilio's answer in PROGRESS (Phase 6 entry check), including whether each organization needs its own registration.

### 4.3 Settings

Local copy (needs a tunnel as in 3.4 for delivery reports and STOP; texts and codes are sent without one):

```powershell
dotnet user-secrets set "Twilio:Mode" "Twilio" --project InpremClockingApp
dotnet user-secrets set "Twilio:AccountSid" "<AC...>" --project InpremClockingApp
dotnet user-secrets set "Twilio:AuthToken" "<auth token>" --project InpremClockingApp
dotnet user-secrets set "Twilio:MessagingServiceSid" "<MG...>" --project InpremClockingApp
dotnet user-secrets set "Twilio:VerifyServiceSid" "<VA...>" --project InpremClockingApp
dotnet user-secrets set "Twilio:WebhookBaseUrl" "https://<tunnel or site address>" --project InpremClockingApp
```

- **Without a Messaging Service:** instead of `MessagingServiceSid`, set `Twilio:FromNumber` to the Twilio number (`+1…`).
- **Without Verify:** leave out `VerifyServiceSid`. The app then makes the codes itself and texts them.
- **With an API key:** also set `Twilio:ApiKeySid` (`SK…`) and `Twilio:ApiKeySecret`. Keep `Twilio:AuthToken`: it checks Twilio's reports and replies.
- **Email through Twilio:** `Email:Provider` = `Twilio`, `Email:From` (the sender address) and optionally `Email:FromName`. This works without `Twilio:Mode` = `Twilio`, so emails can go out while texts stay on the stand-in. If Twilio refuses (`Twilio refused the email (HTTP 400, error …)`), the sender address or its domain is probably not yet set up in the Twilio console.

- `Twilio:WebhookBaseUrl` must be **exactly** the address Twilio calls: https, **no path, no trailing slash**. Twilio's signatures are checked against it; any difference (http vs https, `www.`, a port) makes every report fail with 403.
- On a server: the same keys with `__` (`Twilio__Mode`, `Twilio__AccountSid`, …).
- To go back to the stand-in: `dotnet user-secrets set "Twilio:Mode" "StandIn" --project InpremClockingApp`.

**Limits you may want to raise** (defaults in `src/Inprem.Platform.Application/Notifications/NotificationPorts.cs`, class `NotificationOptions`; change them by setting, not code):

| Setting | Default | Meaning |
|---|---|---|
| `Notifications:DailySpendCapUsd` | `5.00` | At most this much in texts per UTC day for the whole platform; after that, texts are skipped (`daily-spend-cap`) |
| `Notifications:DefaultMonthlySmsLimit` | `50` | Texts per organization per month, unless the console sets another (**Organizations** → **Texts**) |
| `Notifications:EstimatedSmsCostUsd` | `0.02` | Cost counted until Twilio reports the real price |
| `Notifications:QuietHoursStart` / `QuietHoursEnd` | `21:00` / `08:00` | In each organization's time zone |

---

## 5. Testing after the changes

### 5.1 Automated tests (no keys needed; run them after any change)

From the repository folder, one suite at a time:

```powershell
dotnet build InpremClockingApp.sln
dotnet test tests/Inprem.Platform.Tests.Unit --no-build
dotnet test tests/Inprem.Platform.Tests.Architecture --no-build
dotnet test tests/InpremClockingApp.Tests.Baseline --no-build
dotnet test tests/Inprem.Platform.Tests.Integration --no-build
dotnet test tests/InpremClockingApp.Tests.Ui --no-build --filter Category=Ui
```

Expected on 2026-10-20: Unit 262, Architecture 11, Baseline 314, Integration 327, UI 13, all passing.

- The browser tests need the browser once per machine: `pwsh tests/InpremClockingApp.Tests.Ui/bin/Debug/net8.0/playwright.ps1 install chromium`.
- Close the running app first, or the build may fail because its files are locked.
- **If a Baseline test fails, the approved app's behaviour changed: find out why. Don't change the test** (`docs/multitenancy/build/TESTING.md`).

### 5.2 Manual tests with the real services

Use the test guides in `docs/multitenancy/testing/`. The ones that were Blocked:

| With | Tests |
|---|---|
| PayPal sandbox | T2-4, T2-6, T2-7, T2-8, T3-5 to T3-9, T4-8, T4-9, T4-10 (after a month), TC-4 |
| Twilio | T5-2 to T5-4 again with your real phone (codes now come by text), T5-6 (real text), T5-10 (STOP/START), T6-9, TC-4 |

**Also confirm in the PayPal sandbox** (listed in `paypal-notes.md` as "to confirm"):
1. Refund a PayPal-paid invoice (T4-9). The refund must succeed: this checks that the payment id PayPal lists is the capture id the refund uses.
2. In the month's reconciliation (T4-10), subscription payments and invoice payments must match PayPal's lines (sale id and capture id). If PayPal shows a payment the console doesn't, or the other way round, note both ids.

**Record each result** in `docs/multitenancy/build/PROGRESS.md` under "Manual / sandbox test records", with the date. That closes these exit checks:
- P2-1, P2-4 (PayPal subscriptions);
- P3-1 (website journeys);
- P4-1, P4-3 (invoices and reconciliation);
- P5-1 (Verify), P5-4 (delivery);
- P6-3 on staging.

---

## 6. Database and migrations

### 6.1 Are there new tables for PayPal and Twilio?

**No.** All tables are already in migrations M1a to M1p and M2 to M5, shipped as the bundles:

| Bundle | Brings |
|---|---|
| `phase0-up.sql` | Platform schema, memberships, unique emails, composite keys, provider security, Hangfire |
| `phase1-up.sql` | Plans, organizations, support sessions |
| `phase2-up.sql` | PayPal billing, checkouts, old billing moved |
| `phase3-up.sql` | Website API tokens, signups, billing-access codes |
| `phase4-up.sql` | Invoices, refunds |
| `phase5-up.sql` | Messages, consent, texts settings, texted sign-in, alert choices |
| `phase6-up.sql` | Staff alert settings |

Each bundle checks it runs on the right database, is safe to run twice, and has a `-down.sql` to undo it. Details per migration: `scripts/migrations/README.md`.

**Which database has what (2026-10-20):**
- **Your local `DB_A65635_inpremdb`:** Phases 0 to 6, all applied.
- **The SmarterASP test site's database:** Phases 0 to 4 (deployed 2026-10-18). **It still needs `phase5-up.sql` and `phase6-up.sql`** before the current build is deployed there.
- **Production:** none yet; all seven bundles in order.

### 6.2 How to apply a bundle (SSMS)

1. **Back up first.** In SSMS: right-click the database → **Tasks** → **Back Up…**, or on SmarterASP its control panel's backup.
2. Open the bundle file (e.g. `scripts/migrations/phase5-up.sql`) in SSMS.
3. Turn on **Query** → **SQLCMD Mode** (without it, the script prints a warning and does nothing).
4. Choose the right database in the toolbar, click **Execute**.
5. The last line must say "Phase N schema complete." Any error stops the script; nothing after it runs.
6. Run the verify scripts for that phase (`scripts/migrations/platform-M1*-verify.sql`; expected values in `scripts/migrations/README.md`).

From a command line instead:

```powershell
sqlcmd -S <server> -d <database> -U <user> -P <password> -I -b -i scripts/migrations/phase5-up.sql
```

Use `-E` instead of `-U`/`-P` for Windows sign-in.

### 6.3 If you add a table yourself later (e.g. in Phase 7)

1. Change the model (an entity in `src/Inprem.Platform.Domain/`, its mapping in `src/Inprem.Platform.Infrastructure/Persistence/PlatformDbContext.cs`).
2. Create the migration (platform tables). The EF Core 8 tool is at `%USERPROFILE%\.dotnet\ef8\dotnet-ef.exe`; if it is missing, install it with `dotnet tool install dotnet-ef --version 8.0.32 --tool-path $env:USERPROFILE\.dotnet\ef8`.
   ```powershell
   cd src/Inprem.Platform.Infrastructure
   & $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations add M1q_YourName --context PlatformDbContext
   ```
   For the approved app's own tables (`dbo`), use `--context ApplicationDbContext` from `InpremClockingApp/`. The app's tables are frozen: changing them needs a decision first.
3. Make the scripts (from `src/Inprem.Platform.Infrastructure`; `M1p_StaffAlertSettings` is the last migration today):
   ```powershell
   & $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M1p_StaffAlertSettings M1q_YourName --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1q-up.idempotent.sql
   & $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations script M1q_YourName M1p_StaffAlertSettings --idempotent --context PlatformDbContext -o ../../scripts/migrations/platform-M1q-down.idempotent.sql
   ```
   Write `platform-M1q-verify.sql` (read-only counts), like the existing ones.
4. Check nothing is missing: `& $env:USERPROFILE\.dotnet\ef8\dotnet-ef.exe migrations has-pending-model-changes --context PlatformDbContext` must say there are no changes.
5. For a new bundle, copy `scripts/migrations/build-phase6-bundle.ps1` to `build-phase7-bundle.ps1`:
   - add `M1p` to the "before" list and your migration as `$phase7`;
   - new error numbers 50090 to 50094;
   - add it to `.github/workflows/ci.yml` (step "Migration bundles up to date").
6. **Never let the app apply migrations at start-up** (decision D12). Always back up, rehearse on a copy, then apply.

---

## 7. Deployment plan

The general procedure (packages, host settings, checks, rollback) is in `docs/multitenancy/deployment-phase0.md`. The SmarterASP click-by-click guide is the page "Inprem: deploying a test site to SmarterASP". The new addresses are in `docs/multitenancy/runbooks/domain-cutover.md`.

### 7.1 Test site (SmarterASP), now

1. **Back up** the test site's database.
2. Apply `phase5-up.sql`, then `phase6-up.sql` (section 6.2). Check both end with "schema complete".
3. **Publish** the current `mt/integration` build from Visual Studio (Release), as before. Don't tick "remove additional files at destination".
4. **Settings** on the site (control panel, or the server's `appsettings.Production.json`):
   - as today;
   - plus the PayPal settings (3.3) when you have them;
   - plus the Twilio settings (4.3) when you have them;
   - `Links__AppBaseUrl` = `https://<test-site-address>`, `Links__AdminBaseUrl` = `https://<test-site-address>/Provider`;
   - `Jobs__RunServer` = `true`; `Site__SiteMode` = `All`.
   - Template: `scripts/deploy/templates/test-site.single-host.json`.
5. **Restart** the site. Open it: if it fails with "HTTP Error 500.30", a setting is wrong (section 10).
6. **Smoke test:**
   - sign in as an organization admin; open **Dashboard**;
   - clock someone in and out at the kiosk;
   - sign in to the console (`/Provider`).
7. **Webhooks:** point the PayPal webhook (3.2) and the Twilio incoming-messages URL (4.2) at the test site.
8. Run the manual tests (section 5.2), and record the results.
9. **Keep the site awake** (D11): SmarterASP's always-on or keep-alive setting, or an uptime ping every 5 minutes. Background jobs (texts, staff alerts, the daily check) only run while it is awake.

**Rollback:** publish the previous build. If the database must go back too: `phase6-down.sql`, then `phase5-down.sql`, or restore the backup.

### 7.2 Production (after testing, and after Phase 7 if possible)

1. **Decide hosting:** two sites (App and Admin) or one site with `SiteMode=All` and both addresses (`domain-cutover.md`, "Before you start").
2. **Database:**
   - a new production database, or the existing one backed up;
   - all bundles `phase0-up.sql` to `phase6-up.sql` in order;
   - the Hangfire tables come with phase 0.
   - For an existing database with real data: rehearse on a restored copy first (`scripts/rehearsal/rehearse-phase0.ps1 -Existing`; `docs/multitenancy/rehearsal-phase0.md`).
3. **First Provider Owner:** `docs/multitenancy/runbooks/bootstrap-and-recovery.md` section 2 (a one-time setup code; no account is ever seeded). Then invite a second Owner (D16).
4. **Settings:**
   - templates `scripts/deploy/templates/app-site.production.json` and `admin-site.production.json`;
   - secrets per `site-configuration.md`;
   - email (`Email__*`; port 587);
   - `AdminSite__AllowedNetworks__0…` (Inprem's office addresses);
   - PayPal and Twilio as in section 8 when going live.
5. **Packages:** `scripts/deploy/prepare-site.ps1` (`deployment-phase0.md` section 2). Deploy the same build to both sites.
6. **Addresses:** follow `domain-cutover.md` steps 1 to 6. Run `pwsh scripts/deploy/check-cutover.ps1 -Stage …` after each step.
7. **Before the first real customer:** the decisions in section 11 answered, `PayPal:Mode=Live` (section 8), Twilio sender registration approved.

---

## 8. Going live (real money, real texts)

Only with the project owner's written go-ahead (guardrails: no step turns live on by itself).

### PayPal live

1. In the PayPal developer dashboard, switch to **Live**, create the live app, and repeat 3.2: features, webhook to `https://app.inpremclockin.org/webhooks/paypal`, its ID.
2. Settings on the App site **and** the Admin site:
   ```
   PayPal__Mode = Live
   PayPal__AllowLive = true
   PayPal__ClientId = <live client id>
   PayPal__Secret = <live secret>
   PayPal__WebhookId = <live webhook id>
   ```
   plus `ReturnUrlAllowList` / `PlanReturnUrl` / `PlanCancelUrl` with the production addresses (`scripts/deploy/templates/app-site.production.json`). Without `AllowLive = true`, the app refuses to start.
3. **Important:** plans published while in Sandbox keep sandbox ids. A database that was used with the sandbox must forget them before going live, or checkouts fail ("RESOURCE_NOT_FOUND"). In SSMS, on that database, after a backup:
   ```sql
   -- Forget sandbox PayPal ids, so "Publish to PayPal" creates live ones.
   UPDATE platform.PlanPrices SET PayPalPlanId = NULL WHERE PayPalPlanId IS NOT NULL;
   UPDATE platform.Plans SET PayPalProductId = NULL WHERE PayPalProductId IS NOT NULL;
   ```
   Then in the console, **Publish to PayPal** each plan again.
   - Sandbox subscriptions, checkouts and payments in that database are test data: start production from a clean database, or ask a developer to clear them.
   - A fresh production database that never saw the sandbox needs none of this.

### Twilio live

1. Upgrade the Twilio account from trial (add billing).
2. Sender registration approved (4.2 step 5).
3. Production webhook: the Messaging Service's incoming URL `https://app.inpremclockin.org/webhooks/twilio/inbound`, and `Twilio__WebhookBaseUrl` = `https://app.inpremclockin.org` on both sites.
4. Raise `Notifications__DailySpendCapUsd` to what Inprem accepts.

---

## 9. What is left to build: Phase 7

Plan: `docs/multitenancy/build/10-phase7-hardening.md`. Its entry condition is that the Phase 0 to 6 exit checks pass (they wait for the tests in section 5). Rules for any change: `docs/multitenancy/build/00-guardrails.md` (frozen files, tests first, PROGRESS).

| Step | What | Files involved | Notes |
|---|---|---|---|
| **7.1 Anti-forgery** (C3-10) | Turn on the anti-forgery token check for all forms and script calls | `InpremClockingApp/Program.cs` lines 112-116: the `IgnoreAntiforgeryTokenAttribute` filter to remove; a new script under `InpremClockingApp/wwwroot/js/security/` | **Decide first** (PROGRESS, 2026-10-20 entry). The back-office pages use `Pages/Shared/_Layout.cshtml`, which is **frozen**. Their changes are sent with `fetch` (e.g. `Pages/Staff.cshtml` around line 200, `Pages/StaffClocking.cshtml` lines 212 and 300, `Pages/Shared/_StaffEditorScript.cshtml` line 186), not jQuery. `Pages/StaffClocking.cshtml.cs` line 7 opts out itself. The kiosk's clock buttons are GET requests (`Pages/StaffClockPage.cshtml` line 74 → `Controllers/ControlsController.cs`), which anti-forgery never checks. Options: inject the script without touching frozen files; get a design revision allowing the layout edit; or check the `Origin` header instead of tokens. Add UI tests for every POST first. |
| **7.2 Kiosk session** (C3-9) | A limited kiosk sign-in that can't reach the back office, ending at day's end | new code + `Kiosk:SessionMode` setting (off by default) | With the setting off, nothing may change (baseline). |
| **7.3 Row-level security** (D5) | SQL Server security policy per organization, if the host allows it | `scripts/inspection/rls-feasibility.sql` (new); a migration; a connection interceptor | First test on a copy of the hosted database whether `CREATE SECURITY POLICY` is allowed; if not, record it and keep today's filters. |
| **7.4 Contract migration** (M6) | Drop the old `dbo.Subscriptions` / `dbo.Invoices` tables and old username indexes | a new app migration; `Models/Billing/*` mappings in `InpremClockingApp/Data/ApplicationDbContext.cs` | Only after one release with M1-M5 in use. Destructive: full backup, rehearsal, restore plan. |
| **7.5 Data lifecycle** | Organization data export (CSV); closing an organization after the 90-day retention (D24), with warning emails at 30 and 7 days | new pages + a job | Keep billing records required for accounting. |
| **7.6 Security review** | Security headers, OWASP ZAP scan, dependency and secret scans, review of every `IgnoreQueryFilters`, a log-scrubbing test | `Hosting/SecurityHeaders.cs`, tests | **Mostly done (2026-10-22)**: headers, the policy (report-only), the review of cross-organization reads and the log check. Left: the ZAP scan, switching the policy to Enforce after clean reports. See `docs/multitenancy/security-review.md`. |
| **7.7 Operations** | `/health` checks for database, Hangfire, PayPal, Twilio; complete runbooks; final regression and `docs/multitenancy/build/DEMO-SCRIPT.md` on the test site | | Record in PROGRESS. |

**Known small gaps** (PROGRESS, "Open follow-ups"):
- the people limit of a plan is not enforced yet (it needs a change to the approved add-person flows);
- the support history page has no menu link;
- refused cross-organization saves answer 500 rather than 403/404.

---

## 10. Possible errors and how to fix them

### The app doesn't start

| What you see | Cause | Fix |
|---|---|---|
| IIS / SmarterASP: **HTTP Error 500.30 - ASP.NET Core app failed to start** | A setting failed the start-up check, or the database is unreachable | Turn on the log: in the site's `web.config`, `stdoutLogEnabled="true"` and `stdoutLogFile=".\logs\stdout"`, restart, read the newest file in `logs\`. The message names the setting. Turn it off again afterwards. Locally, the terminal shows the message. |
| `PayPal:Mode is Live but PayPal:AllowLive is not true` | Live without the go-ahead switch | Set `PayPal:AllowLive=true` (live only), or `PayPal:Mode=Sandbox` |
| `PayPal:ClientId is set but PayPal:Secret is missing` (or the reverse) | One of the pair missing | Set both, or neither |
| `PayPal:ReturnUrlAllowList entry '…' must be an https origin…` | A path, a trailing `/`, or http | Only `https://host` (e.g. `https://localhost:7078`) |
| `PayPal:PlanReturnUrl must be an https address on an origin in PayPal:ReturnUrlAllowList` | The return page's host is not on the allow-list | Add that origin to `ReturnUrlAllowList` |
| `Twilio:AccountSid must be the account's SID (AC…, 34 characters)` (same for `MessagingServiceSid` MG…, `VerifyServiceSid` VA…, `ApiKeySid` SK…) | Wrong or truncated value, or a space | Copy the SID again from the Twilio console |
| `Twilio:MessagingServiceSid or Twilio:FromNumber is required…` | Neither is set | Set one of them |
| `Twilio:ApiKeySid and Twilio:ApiKeySecret go together…` | Only one of the pair | Set both, or neither |
| `Twilio:WebhookBaseUrl is required with Twilio:Mode = Twilio…` / `…must be the site's https address without a path…` | Missing, or with a path or trailing slash | `https://your-address` exactly |
| `Jobs:RunServer must be false on the Admin site…` | Jobs switched on for the Admin site | `Jobs__RunServer=false` on Admin; jobs run on the App site only |
| `LegacyRedirect:…` messages | The old-address redirect is on with wrong values | `docs/multitenancy/runbooks/root-redirects.md`, or `LegacyRedirect__Enabled=false` |

### PayPal

| What you see | Cause | Fix |
|---|---|---|
| "PayPal is not configured on this site" | No `PayPal:ClientId` | Set the keys, restart |
| **Publish to PayPal** fails, the log shows 401 / `invalid_client` | Wrong keys, or live keys in Sandbox mode (or the reverse) | Copy both keys from the same app and the same mode |
| `PayPal:PlanReturnUrl is not set…` when publishing | Missing setting | Set `PlanReturnUrl` and `PlanCancelUrl` (3.3) |
| Website checkout refused with `return_url_not_allowed` | The website's return address is not on the allow-list | Add its origin to `PayPal:ReturnUrlAllowList` |
| **Webhook events** stays empty after a payment | PayPal can't reach the address, or the webhook isn't subscribed to the event | Check the webhook URL and events (3.2). PayPal's dashboard shows each delivery attempt (**Webhooks events** / "Event logs"). Meanwhile the jobs correct the state within 5 minutes or by the next day |
| Every webhook refused; Owners get "[Inprem] System problem: …webhook…" | `PayPal:WebhookId` is not the ID of **this** URL's webhook | Copy the ID of the webhook whose URL is this site's |
| Checkout fails with `RESOURCE_NOT_FOUND` after switching Sandbox → Live | Sandbox plan ids in the database | Section 8, the SQL, then **Publish to PayPal** again |
| Reconciliation: red "PayPal is not configured" or a permission error | Transaction Search not enabled for the app, or still activating | Enable it in the app's features; wait a few hours |
| Refund of an invoice payment fails | The id assumption in `paypal-notes.md` ("to confirm") is wrong for this case | Note the invoice number and PayPal's ids, refund in PayPal's own site meanwhile, and get a developer to adjust `PayPalInvoiceProvider` |

### Twilio

| What you see in **Notifications** → **Delivery log** (Why / error) | Cause | Fix |
|---|---|---|
| `21608` (trial account) | The number is not a verified caller ID | Verify it in the Twilio console, or upgrade the account |
| `21610` / `opted-out` | The person replied STOP | Working as designed; they reply START to receive texts again |
| `21211` / `invalid-phone` | Not a valid number (often a missing country code) | Fix the number in **Manage Staff** / **Manage Volunteers**, with `+` and the country code |
| `20003` | Wrong Account SID or Auth Token | Copy them again; restart |
| `30032`, `30034` | US sender not registered | Sender registration (4.2 step 5) |
| `30007` | The carrier filtered the text as spam | Check the wording; registration helps |
| `no-consent` | The person never ticked the box | Working as designed |
| `quiet-hours`, `sms-off`, `monthly-limit`, `daily-spend-cap`, `template-off` | The app's own rules | Settings in the console or section 4.3 |
| Texts stay **Sent**, never **Delivered** | Delivery reports don't arrive: `Twilio:WebhookBaseUrl` differs from the address Twilio calls, or no public address | Make it exactly the site's address; for local, a tunnel |
| STOP replies don't appear under **Opt-outs** | The Messaging Service's incoming URL is not `/webhooks/twilio/inbound` on this site | Section 4.2 step 3 |
| Sign-in codes don't arrive; the page says the code service could not be reached | Wrong `VerifyServiceSid` (`20404`), or Twilio down; without Verify, the text was refused (see the log line "Twilio refused a text") | Check the SID or the number; the authenticator app still works for console sign-in |
| A code that just arrived says "expired" (without Verify) | The site restarted after the code was sent (codes are kept in memory) | Ask for a new code |
| `Email is not configured: with Email:Provider = Twilio…` | `Email:From` or the Twilio sign-in is missing | Set `Email:From`, and the API key or the Account SID and Auth Token |
| `Twilio refused the email (HTTP …, error …)` | Usually the sender address or domain is not set up at Twilio, or wrong credentials (401) | Check the sender in the Twilio console; check the credentials |
| "Too many codes asked for" | 3 codes in 10 minutes or 10 a day per account (on purpose) | Wait |

### Database

| What you see | Cause | Fix |
|---|---|---|
| A bundle prints "SQLCMD Mode is not enabled - nothing will be executed" | SSMS not in SQLCMD mode | **Query** → **SQLCMD Mode**, run again |
| Error **50080** (or 50070, 50060…): "schema is not there / not complete" | An earlier bundle is missing, or the wrong database is selected | Check the database name; apply the earlier bundles in order |
| Error **50081** / **50082**: "migrations this bundle does not know" | The database is newer than the bundle (a newer build was deployed) | Use the bundles of that newer build |
| Error 50003 (Phase 0) | Two accounts share an email | `scripts/inspection/04-data-quality.sql` lists them; merge first |
| After deployment: errors such as "Invalid object name 'platform.…'" or "Invalid column name" | The build is newer than the database | Apply the missing bundle |

---

## 11. Other things to know

- **Decisions waiting for the project owner** (record answers in PROGRESS):
  1. approval of the staff texts box (C3-8) and of the sender registration (design P6-1);
  2. the missed clock-out default: 21:00 is the start of quiet hours, so with the default no reminder is ever sent (D32; suggest 20:30);
  3. billing during a suspension (pause, refund, cancel?) and when Inprem may suspend (terms of service);
  4. whether plans with a people limit will be sold (the limit isn't enforced yet);
  5. the live go-ahead for PayPal and Twilio.
- **Branches:**
  - `mt/integration` holds everything. GitHub's copy (`origin/mt/integration`) is at `264b053` (2026-10-18); the 16 commits since are local only.
  - Push with `git push origin mt/integration` when you are ready. The repository is public (D33 recommends making it private before billing code is merged), so make it private first.
  - Never merge into `main` or `fix/login-admin-only` without the CTO.
- **Where things are:**
  - plan and rules: `docs/multitenancy/build/`;
  - where the work stands and every past decision: `PROGRESS.md`;
  - test guides: `docs/multitenancy/testing/`;
  - runbooks (bootstrap and recovery, cutover, redirects, settings): `docs/multitenancy/runbooks/`;
  - migrations: `scripts/migrations/`;
  - deployment scripts: `scripts/deploy/`.
- **Your local secrets** (`dotnet user-secrets list --project InpremClockingApp`) include `Bootstrap:CodeHash` and `SeedAdmin:*` from early testing. They are harmless locally (no account is seeded any more), but never copy them to a server.
- **If you continue with Claude Code later,** start with:
  > Read `docs/multitenancy/build/00-guardrails.md`, `DECISIONS.md`, `TESTING.md`, `PROGRESS.md` and `docs/multitenancy/HANDOVER.md`. Then continue with the next step.
