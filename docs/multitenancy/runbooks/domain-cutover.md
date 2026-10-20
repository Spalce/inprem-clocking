# Domain cutover: inpremclockin.org → app. and admin.

**What changes:** today the whole app is at `inpremclockin.org`. After the cutover:

| Address | Serves |
|---|---|
| `app.inpremclockin.org` | The app organizations use: kiosk, back office, reports, Billing, the website API, PayPal and Twilio webhooks |
| `admin.inpremclockin.org` | Inprem's Provider Console only, open only from Inprem's addresses |
| `inpremclockin.org` (and `www.`) | The web team's marketing website, once it is ready. Until then it keeps serving the app |

**Who does what:** the developer makes the DNS changes and deployments; this runbook says how. The web team adds redirect rules on their hosting (`root-redirects.md`). Nothing here changes data, and there is no database step: the database is the same for all addresses.

**Order:** staging first, then production. Each step can be undone on its own (see "Undo" under each step).

**Tools:**
- `scripts/deploy/check-cutover.ps1` checks the live addresses after each step. It is read-only and prints PASS or FAIL.
- `site-configuration.md` lists every setting.
- `deployment-phase0.md` covers packaging and upload.

---

## Before you start

1. **Access:** the place where the domain's DNS is managed (the registrar or DNS host for `inpremclockin.org`) and the hosting control panel.
2. **One site or two?**
   - **Two sites (the design):** an App site and an Admin site, from the same build. Use this if the hosting plan allows two sites.
   - **One site (D9 fallback):** one site with `Site:SiteMode = All`, bound to both `app.` and `admin.`, with `Site:AppHost` and `Site:AdminHost` set. Each address then serves only its own part. The console stays under `/Provider` (e.g. `https://admin.inpremclockin.org/Provider/Login`). Use this if the plan allows only one site.
3. **Database:** the site must already have all phase scripts applied (Phase 0 to 6 bundles, `scripts/migrations/README.md`).
4. **Addresses for the Admin site's allow-list:** the public IP addresses Inprem's team works from. Ask your internet provider, or open a "what is my IP" page from each place.

## Step 1 — DNS records and certificates

1. At the hosting control panel, add `app.inpremclockin.org` to the App site and `admin.inpremclockin.org` to the Admin site (one site: both to it). The panel shows the address (IP or host name) that the DNS records must point to.
2. At the DNS host, create two records as the panel says:
   - an `A` record (to an IP), or a `CNAME` (to a host name), for `app`;
   - the same for `admin`.
   Do **not** change the record for `inpremclockin.org` or `www`.
3. Wait until the names resolve (usually minutes, at most a day). Check with `Resolve-DnsName app.inpremclockin.org`.
4. In the hosting panel, add an SSL/TLS certificate for each new name (many hosts offer a free one). Both must open with `https://` without a browser warning.

**Undo:** delete the two DNS records. Nothing else used them yet.

## Step 2 — Deploy the App site and the Admin site

1. Make the two packages (`deployment-phase0.md`, section 2):
   ```powershell
   pwsh scripts/deploy/prepare-site.ps1 -Artifact out/app -SiteMode App   -OutDir out/sites -AdminHost admin.inpremclockin.org
   pwsh scripts/deploy/prepare-site.ps1 -Artifact out/app -SiteMode Admin -OutDir out/sites -AppHost app.inpremclockin.org -AdminHost admin.inpremclockin.org
   ```
   The App package gets **no `-AppHost`** for now. While the root still serves the app, the App site must answer on `inpremclockin.org` too. With `AppHost` set, it would answer 404 there.
2. **Settings:** on each site, set the settings from its template (`app-site.production.json`, `admin-site.production.json`) and the secrets (`site-configuration.md`). Check especially:
   - Admin site: `AdminSite__AllowedNetworks__0`, `__1`, … (Inprem's addresses).
   - Both: `Links__AppBaseUrl` and `Links__AdminBaseUrl` (the new addresses: email links now point there).
3. **Bindings:**
   - App site: `app.inpremclockin.org` **and** the existing `inpremclockin.org` and `www.inpremclockin.org`.
   - Admin site: `admin.inpremclockin.org` only.
4. Upload both packages (the same build), App site first.
5. Run the check:
   ```powershell
   pwsh scripts/deploy/check-cutover.ps1 -Stage Interim
   ```
   Every line must say PASS.
   - "console sign-in answers" may show `403` when you run it from an address that is not on the allow-list. That is correct. Run it once from an allowed address to see `200`.
6. Sign in by hand:
   - `https://app.inpremclockin.org/Identity/Account/Login` with an organization admin account. The back office opens on **Dashboard**; open the kiosk pages from the menu (**Staff Clocking**, **Volunteer Clocking**).
   - `https://admin.inpremclockin.org/Login` with a Provider account, from an allowed address.

**Undo:** redeploy the previous package to the site at `inpremclockin.org`, and remove the `app.`/`admin.` bindings. The root never stopped serving the app.

## Step 3 — Point PayPal and Twilio to app.

When the PayPal and Twilio accounts exist (sandbox/test first):
- **PayPal** (developer dashboard → the app → Webhooks): webhook URL `https://app.inpremclockin.org/webhooks/paypal`. Put the webhook's id in `PayPal__WebhookId` on both sites.
- **Twilio** (the Messaging Service's integration settings, and the phone number's messaging settings):
  - status callback `https://app.inpremclockin.org/webhooks/twilio/status`;
  - incoming messages `https://app.inpremclockin.org/webhooks/twilio/inbound`.
  - `Twilio__WebhookBaseUrl` is `https://app.inpremclockin.org` on both sites.
- Send one test from each:
  - PayPal: a sandbox checkout.
  - Twilio: Provider Console → Notifications → **Send me a test message**.
  - Then check that it shows in the console: Notifications → **Delivery log**.

Do this **before** step 5: after the root moves to the website, calls to the old addresses no longer reach the app.

**Undo:** put the old webhook addresses back at PayPal and Twilio.

## Step 4 — Move people to app.

1. **Kiosk devices:** on each device, open `https://app.inpremclockin.org/Identity/Account/Login`, sign in, open the kiosk page as today (**Staff Clocking** or **Volunteer Clocking** in the menu), and bookmark that page's new address. Remove the old bookmark.
2. **Tell the existing organization** (template below).
3. **Optional, recommended:** turn on the App site's own redirect while the root still points to it. Old links then lead straight to `app.`, before the website takes over. On the App site set `LegacyRedirect__Enabled` = `true` and restart it (the other three `LegacyRedirect__` values come from the template). Check:
   ```powershell
   pwsh scripts/deploy/check-cutover.ps1 -Stage Redirect
   ```

**Undo:** `LegacyRedirect__Enabled` = `false`, restart.

**Message to the organization (email or text):**

> Subject: Inprem Clock-in has a new address
>
> Hello,
>
> From today, Inprem Clock-in is at **https://app.inpremclockin.org**. Please use this address for the kiosk and for the back office, and update your bookmarks. Your account, password and records are unchanged.
>
> The old address keeps working for a while and will send you to the new one.
>
> Questions: reply to this message.

## Step 5 — The root becomes the website

When the web team's site is ready (staging first):
1. The web team adds the redirect rules from `root-redirects.md` on their hosting, and checks that their own pages don't collide with the rules.
2. At the DNS host, point `inpremclockin.org` and `www` to the web team's hosting, as they say.
3. When the change has spread (`Resolve-DnsName inpremclockin.org` shows the new address), run:
   ```powershell
   pwsh scripts/deploy/check-cutover.ps1 -Stage Website
   ```
   Every old link must show `301` to the same path on `app.`, and the home page must be the website.

**Undo:** point `inpremclockin.org` and `www` back to the App site (its bindings are still there). The app answers there again at once, with or without the redirect (step 4).

## Step 6 — Tidy up (a week or two later)

1. Remove the `inpremclockin.org` and `www` bindings from the App site.
2. Set `Site__AppHost` = `app.inpremclockin.org` on the App site and restart. From then on, the App site answers only on its own address.
3. Run `check-cutover.ps1 -Stage Website` once more.

**Undo:** `Site__AppHost` empty, restart; add the bindings again.

## If a check fails

| Failing check | Usually means |
|---|---|
| "no answer" | DNS not resolved yet, no certificate, or the site is stopped |
| App site: "sign-in page answers" is 404 | `Site__AppHost` is set to a different name, or the request reached the Admin site (bindings) |
| App site: "no Provider Console here" is not 404 | The App site runs with `SiteMode` `All`: correct only for the one-site setup |
| Admin site: "no back office here" is 200 | The Admin site runs with `SiteMode` `App` or `All` |
| Webhook checks are 404 | The request reached the Admin site, or `Site__AppHost` names another host |
| Root: old link not 301 | Step 4: `LegacyRedirect__Enabled` is not `true`, or the site was not restarted. Step 5: the web team's rules are missing or don't match |
