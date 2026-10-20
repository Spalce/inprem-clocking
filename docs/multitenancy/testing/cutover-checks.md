# Cutover checks (after deployment)

[← All test guides](README.md)

These checks belong to the domain cutover (`docs/multitenancy/runbooks/domain-cutover.md`). After it, the app lives at `app.inpremclockin.org`, the Provider Console at `admin.inpremclockin.org`, and `inpremclockin.org` becomes the website.

They can only run after the DNS change and the deployment; until then mark them **Blocked**. Staging first, with the staging addresses.

Each step of the runbook ends with the check script. It only reads the addresses (GET requests), never follows redirects, and prints PASS or FAIL per line:

```powershell
pwsh scripts/deploy/check-cutover.ps1 -Stage Interim      # after runbook step 2
pwsh scripts/deploy/check-cutover.ps1 -Stage Redirect     # after step 4
pwsh scripts/deploy/check-cutover.ps1 -Stage Website      # after step 5
```

For staging add `-AppHost …`, `-AdminHost …` and `-RootHost …`.

---

## TC-1 The new addresses answer

**Use case:** after the move, admins and kiosks work at `app.`, and Inprem's staff at `admin.`.

1. Run `check-cutover.ps1 -Stage Interim`.
   - **What it does:** opens the sign-in page, a kiosk page, the console address and the webhook addresses on each site, and checks the answer.
2. Open `https://app.inpremclockin.org/Identity/Account/Login` and sign in as an organization admin. Open **Dashboard** (`/BackOffice`) and a kiosk page (**Staff Clocking**).
3. Repeat T0-5 (kiosk clocking) and T0-7 (one PDF) on `app.`.
4. Open `https://admin.inpremclockin.org/Login` and sign in to the console.

**Expected:**
- Every line of the script says PASS.
- Steps 2 to 4 work as on the test copy.
- The browser shows no certificate warning on either address.

## TC-2 The Admin site is only open from allowed addresses

**Use case:** the console controls every organization, so only Inprem's offices may reach it.

1. From an address on the allow-list (`AdminSite:AllowedNetworks`), open `https://admin.inpremclockin.org/Login`.
2. From a phone on mobile data (not on the list), open the same address.
3. On `admin.`, type `/Identity/Account/Login` after the address.

**Expected:**
- Step 1 shows **Provider Console**.
- Step 2 is refused (403) before any page loads.
- Step 3 is "not found": the organizations' app is not on `admin.`.

## TC-3 Old links move to app.

**Use case:** kiosk tablets and bookmarks still point at `inpremclockin.org/...`; they must keep working.

1. After runbook step 4 (the app's own redirect on) or step 5 (the website's rules), run `check-cutover.ps1 -Stage Redirect` (or `-Stage Website`).
   - **What it does:** opens ten old links (back office, kiosk pages, a kiosk clock page, sign-in with its return address, reports, Billing, an API address) on the root, and checks each answers **301** to the same path on `app.`.
2. In a browser, open an old bookmark such as `https://inpremclockin.org/StaffAttendance`.
3. Open `https://inpremclockin.org/`.

**Expected:**
- All PASS.
- Step 2 ends on `https://app.inpremclockin.org/StaffAttendance`.
- Step 3 shows the website (stage Website) or the app's start page (stage Redirect), never a redirect.

## TC-4 Webhooks reach app. (sandbox, Twilio)

**Use case:** PayPal and Twilio report payments and text deliveries to the app's new address.

1. After runbook step 3, do T2-7 (sandbox subscription) and T5-6 with Twilio connected (T5-10).
2. In the console, open **Webhook events** and **Notifications** → **Delivery log**.

**Expected:** the PayPal event is listed as processed, and the text turns **Delivered**, both received at `app.`.
