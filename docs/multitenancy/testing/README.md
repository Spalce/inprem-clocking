# Test guides: everything built so far (Phases 0 to 6 and the cutover)

These guides let you check by hand everything the multi-tenancy work has built. Each test:
- starts with a **use case**: who needs it and why;
- gives the **exact clicks**, using the names the app shows;
- after each step, says **what that click does** behind the scenes;
- ends with what you should see (**Expected**).

Record the result of each test in the [results sheet](#results-sheet) below.

| Guide | What it covers | Time |
|---|---|---|
| [Phase 0: everyday clocking](phase0-everyday-clocking.md) | Sign-in, kiosk, back office, reports: what staff, volunteers and admins use every day | 45 min |
| [Phase 1: organizations and the console](phase1-organizations-console.md) | Separate organizations, the Provider Console, plans, support sessions, the audit log | 60 min |
| [Phase 2: billing and unpaid accounts](phase2-billing.md) | The Billing page, trials, free access, read-only accounts, PayPal subscriptions | 30 min |
| [Phase 3: website sign-up](phase3-website-signup.md) | Sign-up and subscription management from the (demo) website | 30 min |
| [Phase 4: invoices and refunds](phase4-invoices.md) | Invoices, offline payments, refunds, the monthly reconciliation | 30 min |
| [Phase 5: messages and texts](phase5-messages-texts.md) | Emails and texts, sign-in codes by text, the console's Notifications pages | 45 min |
| [Phase 6: staff alerts](phase6-staff-alerts.md) | Reminders to staff and volunteers still clocked in or on a long break | 45 min |
| [Cutover checks](cutover-checks.md) | The new addresses `app.` and `admin.`, after deployment | 20 min |

Test IDs T0-1 to T4-10 are the same as in the earlier "test guide (Phases 0 to 4)". Results you already recorded there still count. Re-test the ones that failed: they were fixed (see [Known issues](#known-issues)).

---

## Before you start

**Test on a test copy only, never on the live site.** Use your local copy (below) or the SmarterASP test site. Both use made-up data.

### The local copy

1. Open a terminal in the repository folder and run:
   ```powershell
   dotnet run --project InpremClockingApp --launch-profile https
   ```
   **What it does:** starts the app at `https://localhost:7078`, with your user-secrets.
2. Open `https://localhost:7078` in the browser. This is **the address** in every test below.

**The local database needs every phase script first.** The app never changes the database by itself. Phases 0 to 6 are already applied to your `DB_A65635_inpremdb` (checked 2026-10-20). For a new database, run the bundles `phase0-up.sql` to `phase6-up.sql` in order (`scripts/migrations/README.md`).

**Already set in your local user-secrets** (checked 2026-10-20; only the names were read, not the values):

| Setting | What it gives you |
|---|---|
| `Site:SiteMode` = `All` | The organizations' app and the Provider Console on one address. The console is under `/Provider` |
| `Jobs:RunServer` = `true` | Background jobs run: texts are sent, staff alerts are checked, the daily billing check runs |
| `Email:DropFolder` | **Emails are saved as `.html` files** in this folder instead of being sent. Open them with a double-click |
| `Twilio:StandInFolder` | **Texts are saved as `.txt` files** in this folder instead of being sent: `...-sms-<number>.txt` for messages, `...-code-<number>.txt` for sign-in codes |
| `Demo:EnableDemoSite`, `Captcha:Secret` | The demo website at `/demo-site`, with a CAPTCHA test key that always passes |
| `Links:*` | The addresses used in links inside emails |

To see the two folders' paths: `dotnet user-secrets list --project InpremClockingApp`.

### What you need

| Item | Who provides it | Used in |
|---|---|---|
| The address (local: `https://localhost:7078`) | Developer | All tests |
| A Provider Console account with the **Owner** role, with the authenticator app on your phone | Exists on the local copy | Phases 1 to 6 |
| Two organizations, **Alpha** and **Beta**, each with an owner account, a few staff and volunteers (local copy: **Acme Test Org** as Alpha, **AKIM ASAFO SHS** as Beta) | Developer | Phases 0, 1, 2, 4, 6 |
| An organization **you create** in T1-5, with a free trial | You | Phases 1, 2, 5, 6 |
| Email addresses to sign up with (they don't need to be real locally: emails land in the drop folder) | You | Phases 1, 3 |
| PayPal sandbox keys | Project owner | Tests marked **(sandbox)** only |
| Twilio test credentials | Project owner | Tests marked **(Twilio)** only |

**Tip:** use a normal browser window for one account and a private window (Ctrl+Shift+N) for a second account at the same time.

### Getting around the app (organization admins)

1. Open the address. **Admin Log in only** appears. Type the **Email** and **Password** and click **Log in**.
   - **What it does:** checks the password. An account in two organizations then sees **Choose organization** and picks one. An account in one organization goes straight in.
2. You land on the kiosk view (**Volunteer Clocking**). Its left menu has only **Volunteer Clocking** and **Staff Clocking**.
3. To reach the back office, add `/BackOffice` to the address (e.g. `https://localhost:7078/BackOffice`) and press Enter.
4. The back office's left menu:
   - **Dashboard**, **Staff Clockings**, **Volunteer Clockings**, **Manage Staff**, **Manage Volunteers**, **Admins**;
   - **Billing** and **Staff alerts**: the organization's owner only;
   - **Reports**: click it to open **Staff List**, **Staff Clocking**, **Volunteer List** and **Volunteer Clocking**.
5. To sign out, click **Logout** (top right).

### Getting around the Provider Console (Inprem staff)

1. Add `/Provider` to the address. **Provider Console** appears. Type your **Email** and **Password**, click **Sign in**, then type the 6-digit **Authenticator code** from your phone and click **Sign in**.
2. The left menu: **Dashboard**, **Organizations**, **Plans**, **Billing**, **Webhook events**, **Notifications**, **Jobs**, **Audit log**, **Provider users**, **Settings**. You only see what your role allows (Support < Billing < Owner).
3. At the top: **My phone for codes**, **My alerts** and **Sign out**.

### Pages with no menu link

Type these after the address:

| Address | What it is |
|---|---|
| `/Account/Invitations` | Invitations to join another organization |
| `/Account/SupportSessions` | Visits by Inprem's support staff to your organization |
| `/StaffAttendance`, `/VolunteerAttendance` | The kiosk pages, from the back office |
| `/demo-site` | The demo website (test copies only) |
| `/Provider/jobs` | The background jobs dashboard (Provider Owners) |

### How to record results

- Do the guides in order: later tests reuse what earlier ones created.
- In the results sheet write **Pass**, **Fail**, **Partly** or **Blocked** (Blocked: something the test needs is missing, e.g. PayPal keys).
- For a Fail, write what you saw and take a screenshot. If a red error page appears, copy its "Request ID".
- No real money is involved. All payments use the PayPal sandbox or no PayPal at all.

---

## Known issues

These are known and not yet fixed. You don't need to report them again.

| Where | What you see | Status |
|---|---|---|
| Local copy only: after a support session (T1-8) | The console asks you to sign in again, or shows **No access**. On one address the support session replaces your console sign-in (your T1-8 note). Production has separate addresses and is not affected | By design for one-address copies |
| Console → **Extend trial** | Only on the **Subscription** tab, for an organization on a free trial (or whose trial ended) that does not pay by PayPal. Existing organizations are on **Free**, so they don't show it (your T2-2 note). Use the organization you create in T1-5 | Works as designed |
| Owner texts | The demo website never asks the owner to agree to texts, so trial and billing alerts reach owners by email only in these tests | Website decision |

**Fixed since your earlier results:**
- T1-3: accepting an invitation now shows **Choose organization** (2026-10-16).
- T2-3: a suspended organization is read-only, with a banner (2026-10-17).
- T1-6: an organization's **Audit** tab shows who by email, like the **Audit log** page (2026-10-20).

---

## Not testable yet

| What waits | Tests | What unblocks it |
|---|---|---|
| Real PayPal payments: publish plans, subscribe, cancel, renewals, failed payments, PayPal invoices and refunds | T2-4, T2-6 to T2-8, T3-5 to T3-9, T4-8 to T4-10 | PayPal sandbox keys from the project owner |
| Real texts to phones; STOP and START replies; delivery reports | T5-10, T6-9 | Twilio test credentials and a public address for the test copy |
| The new addresses `app.` and `admin.` | All cutover checks | DNS, certificates and deployment (`docs/multitenancy/runbooks/domain-cutover.md`) |
| The web team's real website | All of Phase 3 | The web team's site on a test address |

Automated tests cover all of these with a pretend PayPal and a pretend Twilio: 927 tests, all passing on 2026-10-20.

---

## Results sheet

Tester: ______ · Address: ______ · Date: ______

| Test | What it checks | Result | Notes |
|---|---|---|---|
| T0-1 | Sign in, sign out, lockout | | |
| T0-2 | Forgot password | | |
| T0-3 | Dashboard | | |
| T0-4 | Add and edit people | | |
| T0-5 | Kiosk clocking | | |
| T0-6 | Attendance and manual entries | | |
| T0-7 | Reports and downloads | | |
| T0-8 | Times in the organization's time zone | | |
| T0-9 | Kiosk registration of a new person | | |
| T1-1 | One organization cannot see another's data | | |
| T1-2 | Someone in two organizations chooses one | | |
| T1-3 | Inviting another admin | | |
| T1-4 | Console sign-in needs the two-step code | | |
| T1-5 | Create an organization (wizard) | | |
| T1-6 | Manage an organization | | |
| T1-7 | Plans | | |
| T1-8 | Support session | | |
| T1-9 | Provider users and the audit log | | |
| T1-10 | Console dashboard | | |
| T1-11 | Sign in with a recovery code | | |
| T2-1 | The in-app Billing page | | |
| T2-2 | Extend a trial and give free access | | |
| T2-3 | An unpaid or suspended account becomes read-only | | |
| T2-4 | Cancel from the console (sandbox) | | |
| T2-5 | Webhook events page | | |
| T2-6 | Publish a plan to PayPal (sandbox) | | |
| T2-7 | Subscribe from the Billing page (sandbox) | | |
| T2-8 | Cancel at PayPal (sandbox) | | |
| T3-1 | Pricing page | | |
| T3-2 | Sign up for a free trial | | |
| T3-3 | Mistakes on the form | | |
| T3-4 | Manage-subscription link | | |
| T3-5 | Pay after a trial (sandbox) | | |
| T3-6 | Sign up and pay now (sandbox) | | |
| T3-7 | Change plan and cancel (sandbox) | | |
| T3-8 | Cancel at PayPal (sandbox) | | |
| T3-9 | Payment problem (sandbox) | | |
| T3-10 | Demo website off unless switched on | | |
| T4-1 | Create and send an offline invoice | | |
| T4-2 | Record offline payments | | |
| T4-3 | Cancel an unpaid invoice | | |
| T4-4 | The organization sees its invoices | | |
| T4-5 | Refund an offline payment | | |
| T4-6 | Only Owners can refund | | |
| T4-7 | Monthly reconciliation report | | |
| T4-8 | PayPal invoice paid at PayPal (sandbox) | | |
| T4-9 | Refund through PayPal (sandbox) | | |
| T4-10 | Reconciliation equals PayPal (sandbox) | | |
| T5-1 | Where emails and texts go (stand-in) | | |
| T5-2 | Your phone for sign-in codes | | |
| T5-3 | Console sign-in with a texted code | | |
| T5-4 | Texted sign-in codes for an organization's admins | | |
| T5-5 | My alerts | | |
| T5-6 | Send me a test message; the Delivery log | | |
| T5-7 | Message wording | | |
| T5-8 | An organization's texts: on, off, monthly limit | | |
| T5-9 | Billing alerts (trial ending, suspended, invoice) | | |
| T5-10 | Real texts, STOP and START (Twilio) | | |
| T6-1 | Staff alerts settings (owner only) | | |
| T6-2 | Alerts off: forms unchanged | | |
| T6-3 | Kiosk registration with the texts box | | |
| T6-4 | Manage Staff / Manage Volunteers with the texts box | | |
| T6-5 | Missed clock-out reminder | | |
| T6-6 | Long-break reminder | | |
| T6-7 | End-of-day summary to admins | | |
| T6-8 | Quiet hours and no consent | | |
| T6-9 | Real staff texts (Twilio) | | |
| TC-1 | New addresses answer (after deployment) | | |
| TC-2 | Admin site only from allowed addresses | | |
| TC-3 | Old links move to app. | | |
| TC-4 | Webhooks reach app. (sandbox, Twilio) | | |
