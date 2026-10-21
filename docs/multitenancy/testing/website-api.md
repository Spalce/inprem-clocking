# Website API: direct calls (PowerShell)

[← All test guides](README.md) · [The same API on its docs page (Swagger)](website-api-swagger.md)

The [Phase 3 guide](phase3-website-signup.md) tests the website API through the demo website. This guide calls the API **directly**, the way the web team's site will, and checks the answers the demo website never shows:
- the error codes;
- the tokens;
- the `Idempotency-Key` header;
- the limits.

The reference for every endpoint is [`docs/multitenancy/api/integration-guide.md`](../api/integration-guide.md). Tests marked **(sandbox)** need PayPal sandbox keys.

**Every sign-up in this guide creates a real organization** (Trialing) and an owner account in your local database. Use the local copy only.

---

## Before you start

### TA-0 Set up PowerShell

1. Start the app as in [Before you start](README.md#the-local-copy). Leave that terminal open.
2. Open a **second** terminal with **PowerShell 7**: Start menu → type `pwsh` → **PowerShell 7**. Check the version:
   ```powershell
   $PSVersionTable.PSVersion
   ```
   It must be 7 or higher. The older "Windows PowerShell" (5.1) cannot show error answers.
3. Paste this helper and press Enter. It stays until you close the window:
   ```powershell
   $api = "https://localhost:7078/api/public/v1"
   function Call {
     param([string]$Method, [string]$Path, $Body, [string]$Token, [string]$Key)
     $headers = @{}
     if ($Token) { $headers.Authorization = "Bearer $Token" }
     if ($Key)   { $headers["Idempotency-Key"] = $Key }
     $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Depth 5 } else { $null }
     $global:res = Invoke-WebRequest -Method $Method -Uri "$api$Path" -Headers $headers -Body $json -ContentType "application/json" -SkipHttpErrorCheck
     Write-Host "$($res.StatusCode)" -ForegroundColor Cyan
     $text = if ($res.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($res.Content) } else { $res.Content }
     if ($text) { $obj = ConvertFrom-Json $text -NoEnumerate; ConvertTo-Json -InputObject $obj -Depth 6 | Write-Host; return ,$obj }
   }
   ```
   **What it does:** `Call GET /plans` sends a request to the API. It prints the **status** in blue (200, 201, 400, …) and then the **answer**.
   - `-Body` sends JSON, `-Token` a bearer token, `-Key` an `Idempotency-Key`.
   - Write `$x = Call …` to keep the answer in `$x`. The last full answer, with its headers, is in `$res`.

### Things to know

- **At most 10 write calls a minute.** This limit is per computer and covers:
  - `POST /signups`;
  - `POST /billing-access/requests`;
  - `POST /billing-access/sessions`.

  Over it, you get `429` with `"code": "rate_limited"`. Wait a minute and repeat the call; nothing is wrong. The tests below never need more than 10 in a row, except TA-11.
- **Every error** has the same shape: `title` (a human sentence), `status`, `code` (stable, for the website), `traceId` (for support) and, for `validation_failed`, `errors` listing each wrong field.
- **Emails** land in the drop folder as `.html` files (README, [Before you start](README.md#the-local-copy)).

### Where `captchaToken` comes from

On a real website, the visitor's browser shows Cloudflare's Turnstile widget (the "I'm human" box). When the visitor passes it, the widget gives the page a one-time answer, and the website sends that answer as `captchaToken`. The app then asks Cloudflare whether the answer is genuine. The demo website (`/demo-site`) works this way, with Cloudflare's test site key `1x00000000000000000000AA`.

For direct calls there is no widget, and you don't need one. Your local `Captcha:Secret` is Cloudflare's test secret `1x0000000000000000000000000000000AA`, and Cloudflare accepts **any** answer checked with it. So any non-empty text works: this guide uses `"test"`.

| Situation | Answer |
|---|---|
| `captchaToken` empty or missing | `400 validation_failed`, with `errors.captchaToken` "The CAPTCHA answer is required." |
| Your computer is offline, or can't reach Cloudflare | `400 captcha_failed`: the app still asks Cloudflare, even with the test secret |
| `Captcha:Secret` not set | `400 captcha_failed` |
| `Captcha:Secret` set to Cloudflare's always-fail test secret `2x0000000000000000000000000000000AA` | `400 captcha_failed` (how TA-4 shows this error) |

On staging and production a real secret is used. Made-up text then fails, and only an answer from the real widget passes: test there through the demo website or the web team's site.

---

## TA-1 Public reads

**Use case:** the website builds its pricing page and sign-up form before anyone has an account.

1. Check the API is up:
   ```powershell
   Call GET /health
   ```
2. Get the plans and keep the first price for later:
   ```powershell
   $plans = Call GET /plans
   $price = $plans[0].prices[0].planPriceId
   $res.Headers["Cache-Control"]
   ```
   - **What it does:** lists the plans with **Show on the public pricing page** ticked (T1-7), with their prices that are on sale.
3. Get the time zones:
   ```powershell
   Call GET /timezones
   ```
4. Check web names. In order: one that is free, a reserved one, a badly formed one:
   ```powershell
   Call GET /slugs/my-new-centre/availability
   Call GET /slugs/admin/availability
   Call GET "/slugs/Bad Name!/availability"
   ```

**Expected:**
- Step 1: `200` `{ "status": "ok" }`.
- Step 2:
  - `200` and the public plans, each with `trialDays`, `features`, `limits.maxPeople` and `prices`;
  - every price has `planPriceId`, `billingCycle` and `price` (`"amount": "10.00"`, `"currency": "USD"`);
  - the last line prints `public, max-age=300`;
  - no plan without **Show on the public pricing page** is listed.
- Step 3: `200`, a list of `id` (such as `Africa/Accra`) and `displayName`.
- Step 4:
  - `my-new-centre` gives `200` with `"available": true` and `"suggestion": null`;
  - `admin` gives `200` with `"available": false` and `"suggestion": "admin-org"`;
  - `Bad Name!` gives `400` `validation_failed`, with `errors.slug` "Use lower-case letters, digits and single dashes".
- No answer contains anyone's name or email.

## TA-2 Sign up for a free trial (journey J1)

**Use case:** the website registers a new organization on a free trial.

1. Prepare a sign-up. `$n` makes the names unique each time:
   ```powershell
   $n = Get-Random -Maximum 99999
   $signup = @{
     organizationName = "API Test Centre $n"; slug = "api-test-$n"; timeZone = "Africa/Accra"
     owner = @{ firstName = "Ama"; lastName = "Owusu"; email = "ama$n@example.org"; mobile = "" }
     consent = @{ acceptTerms = $true; acceptPrivacyPolicy = $true; smsOptIn = $false }
     planPriceId = $price; mode = "trial"; captchaToken = "test"
   }
   $key = [guid]::NewGuid().ToString()
   ```
   - `captchaToken` can be any text locally: see [Where `captchaToken` comes from](#where-captchatoken-comes-from).
2. Send it:
   ```powershell
   $s = Call POST /signups $signup -Key $key
   $res.Headers["Location"]
   ```
   - **What it does:** checks the fields and the CAPTCHA, then creates:
     - the organization, on a free trial;
     - its owner account, **without a password**;
     - a confirmation email to the owner.

     It also alerts the console users who chose "New signups".
3. Open the drop folder.

**Expected:**
- Step 2:
  - `201`, with `signupId`, `signupToken`, `signupTokenExpiresAt` (2 hours from now), `"status": "PendingVerification"` and `"nextAction": "verify_email"`;
  - the `Location` line is `/api/public/v1/signups/<signupId>`.
- Step 3: a new file **Confirm your email for API Test Centre … on Inprem Clock-in** and, if you chose "New signups" in **My alerts**, **Inprem: New signup API Test Centre …**.
- Console: **Organizations** lists **API Test Centre …**; its **Subscription** tab says Trialing.

## TA-3 The same request twice (Idempotency-Key)

**Use case:** the visitor double-clicks **Register**, or the network drops and the website sends again. Only one organization may be created.

1. No key:
   ```powershell
   Call POST /signups $signup
   ```
2. The same key and the same body as TA-2:
   ```powershell
   $again = Call POST /signups $signup -Key $key
   $again.signupId -eq $s.signupId
   ```
3. The same key with a changed body:
   ```powershell
   $signup.organizationName = "Changed $n"
   Call POST /signups $signup -Key $key
   $signup.organizationName = "API Test Centre $n"
   ```

**Expected:**
- Step 1: `400` `validation_failed`, with `errors."Idempotency-Key"` "An Idempotency-Key header of 8 to 100 characters is required".
- Step 2:
  - `201` with exactly the same answer as TA-2 (the same token too), and `True` on the last line;
  - no second organization in the console, and no second confirmation email.
- Step 3: `422` `idempotency_key_reused` ("This Idempotency-Key was already used with a different request."). Nothing is created.

## TA-4 Mistakes in a sign-up

**Use case:** the website sends a form with mistakes; nothing half-done may be left behind. Each call uses a new key.

1. Everything wrong at once:
   ```powershell
   $bad = @{ organizationName = ""; slug = "Bad Name"; timeZone = "Mars/Base"; owner = @{ email = "nope" }
             consent = @{ acceptTerms = $false }; planPriceId = $price; mode = "trial"; captchaToken = "" }
   Call POST /signups $bad -Key ([guid]::NewGuid())
   ```
2. A taken web name (TA-2's) with a new owner email:
   ```powershell
   $taken = $signup.Clone(); $taken.owner = @{ firstName = "Kofi"; lastName = "Mensah"; email = "kofi$n@example.org" }
   Call POST /signups $taken -Key ([guid]::NewGuid())
   ```
3. A new web name with TA-2's owner email:
   ```powershell
   $usedEmail = $signup.Clone(); $usedEmail.slug = "api-test-b-$n"
   Call POST /signups $usedEmail -Key ([guid]::NewGuid())
   ```
4. A price that does not exist:
   ```powershell
   $noPrice = $taken.Clone(); $noPrice.slug = "api-test-c-$n"; $noPrice.planPriceId = [guid]::NewGuid()
   Call POST /signups $noPrice -Key ([guid]::NewGuid())
   ```

**Expected:**
- Step 1: `400` `validation_failed`. `errors` has one line each for:
  - `organizationName`, `slug` and `timeZone` ("Choose one of the time zones from GET /timezones");
  - `owner.firstName`, `owner.lastName` and `owner.email`;
  - `consent.acceptTerms` and `consent.acceptPrivacyPolicy`;
  - `captchaToken` ("The CAPTCHA answer is required").
- Step 2: `409` `slug_taken` ("\"api-test-…\" is already used.").
- Step 3: `409` `email_in_use` ("An account with this email already exists. Sign in to the app, or use another email.").
- Step 4: `400` `plan_not_available`.
- The console's **Organizations** has no new organization from these four calls.

**Not testable locally:** `captcha_failed`, because the local test key accepts every answer. To see it, the developer temporarily sets `Captcha:Secret` to Cloudflare's always-fail test secret `2x0000000000000000000000000000000AA`. Restart the app, and step 1 of TA-2 with good fields gives `400` `captcha_failed`. Then set it back to `1x0000000000000000000000000000000AA`.

## TA-5 The signup token

**Use case:** after a sign-up, the website checks whether the owner has confirmed their email. Only that sign-up's token may see it.

1. With the token:
   ```powershell
   $t = $s.signupToken; $id = $s.signupId
   Call GET "/signups/$id" -Token $t
   ```
2. Without a token, with a made-up token, and for another sign-up:
   ```powershell
   Call GET "/signups/$id"
   Call GET "/signups/$id" -Token "made-up"
   Call GET "/signups/$([guid]::NewGuid())" -Token $t
   ```
3. Use the signup token on an organization's billing:
   ```powershell
   Call GET /account/subscription -Token $t
   ```

**Expected:**
- Step 1: `200`, with `"status": "PendingVerification"` and `"subscriptionStatus": "Trialing"`.
- Step 2: in order:
  - `401` `token_missing` ("A bearer token is required.");
  - `401` `token_invalid` ("The token is not valid.");
  - `404` `not_found` ("No such signup.").
- Step 3: `401` `token_invalid` ("This token cannot be used here.").

## TA-6 Send the confirmation email again

**Use case:** the owner didn't get the email and clicks "Send it again" on the website.

1. Ask four times:
   ```powershell
   1..4 | ForEach-Object { Call POST "/signups/$id/resend-verification" -Token $t }
   ```

**Expected:** `202`, `202`, `202`, then `409` `resend_limit_reached` ("At most 3 verification emails per hour"). The drop folder has three more **Confirm your email…** files. Each one has a new link; any of them works once.

## TA-7 The owner confirms the email

**Use case:** the owner clicks the link and chooses a password; the website sees it.

1. Open the newest **Confirm your email for API Test Centre …** file and click its link.
2. On **Confirm your email**, type a **Password** and **Password again**, click **Confirm and save password**.
3. Back in PowerShell:
   ```powershell
   Call GET "/signups/$id" -Token $t
   Call POST "/signups/$id/resend-verification" -Token $t
   ```

**Expected:**
- Step 2: **You are all set**.
- Step 3:
  - the status call gives `200` with `"status": "Active"` and `"subscriptionStatus": "Trialing"` (Active means "verified and can use the app");
  - the resend gives `409` `already_verified` ("The email is already verified.").
- You can sign in to the app with `ama…@example.org` and the new password.

## TA-8 A manage-subscription link (journey J3)

**Use case:** an existing customer asks the website for a link to manage their subscription. The website must never learn who is a customer.

1. Ask for the TA-2 owner, for someone unknown, and with a bad email:
   ```powershell
   Call POST /billing-access/requests @{ email = "ama$n@example.org"; captchaToken = "test" }
   Call POST /billing-access/requests @{ email = "nobody$n@example.org"; captchaToken = "test" }
   Call POST /billing-access/requests @{ email = "nope"; captchaToken = "test" }
   ```
   - **What it does:** for an owner, a background job emails a one-time link, valid for 15 minutes. The answer is the same either way, and always takes at least 0.4 seconds, so the timing gives nothing away.
2. Wait a few seconds. In the drop folder, open the new **Manage your Inprem Clock-in subscription** file. **Do not click the button.** Clicking opens the demo website, which uses the link at once. Instead, right-click the button → **Copy link address**. Paste it into Notepad and copy the part after `code=`.
3. Exchange the code, then try the same code again:
   ```powershell
   $code = "<paste the code here>"
   $b = Call POST /billing-access/sessions @{ code = $code }
   Call POST /billing-access/sessions @{ code = $code }
   $bt = $b.billingAccessToken
   ```

**Expected:**
- Step 1:
  - the first two calls both give `202` with the same `message`: "If this email belongs to an organization owner, we have sent a link to manage the subscription.";
  - the third gives `400` `validation_failed` (`errors.email`).
- Step 2: exactly one new email, to the owner. Nothing for `nobody…`.
- Step 3: the first exchange gives `201`, with `billingAccessToken`, `expiresAt` (30 minutes from now) and `organizationName` "API Test Centre …". The second gives `400` `invalid_code` ("This link is not valid, was already used or has expired").

## TA-9 The organization's subscription and invoices

**Use case:** with the link, the owner sees their subscription and invoices on the website.

1. With the billing-access token from TA-8:
   ```powershell
   Call GET /account/subscription -Token $bt
   Call GET /account/invoices -Token $bt
   Call POST /account/subscription/cancel -Token $bt
   Call POST /account/subscription/keep -Token $bt
   Call GET "/signups/$id" -Token $bt
   ```
2. Optional: to see invoices, repeat TA-8 with the email of **Acme Test Org**'s owner (the organization that got the Phase 4 invoices). Then run `Call GET /account/invoices -Token $bt` with its token.

**Expected:**
- Step 1:
  - the subscription gives `200`, with:
    - `"status": "Trialing"`;
    - the `plan` chosen in TA-2;
    - `"billingMode": "PayPal"`;
    - `trialEndsAt` (the trial length after TA-2), `"cancelAtPeriodEnd": false` and `outstandingBalance` `0.00 USD`;
  - the invoices give `200` `[]` (a new organization has none);
  - cancel gives `409` `subscription_not_cancellable` ("There is no active PayPal subscription to cancel.": a trial has nothing to cancel);
  - keep gives `409` `cannot_keep`;
  - the last call gives `401` `token_invalid`: a billing token cannot read a sign-up.
- Step 2:
  - every invoice from T4-1 to T4-3 is listed, newest first, with `number`, `status`, `total` and `amountDue`;
  - a refunded one has `amountRefunded`;
  - offline invoices have `"paymentUrl": null`;
  - there are no drafts.

## TA-10 Checkouts without PayPal

**Use case:** before PayPal keys exist, the API must refuse payments cleanly, never half-start them.

1. Use the signup token for a "pay now" checkout and for a "convert" checkout. Then look up a checkout that doesn't exist:
   ```powershell
   $urls = @{ returnUrl = "https://localhost:7078/demo-site/return"; cancelUrl = "https://localhost:7078/demo-site/cancelled" }
   Call POST /checkout-sessions ($urls + @{ planPriceId = $price; purpose = "new" }) -Token $t -Key ([guid]::NewGuid())
   Call POST /checkout-sessions ($urls + @{ planPriceId = $price; purpose = "convert" }) -Token $t -Key ([guid]::NewGuid())
   Call GET "/checkout-sessions/$([guid]::NewGuid())" -Token $t
   ```
2. With the billing-access token, convert the trial:
   ```powershell
   Call POST /checkout-sessions ($urls + @{ planPriceId = $price; purpose = "convert" }) -Token $bt -Key ([guid]::NewGuid())
   ```
3. Without any key header:
   ```powershell
   Call POST /checkout-sessions ($urls + @{ planPriceId = $price; purpose = "convert" }) -Token $bt
   ```

**Expected:**
- Step 1:
  - the first call gives `502` `payment_provider_unavailable` ("Online payment is not available yet. Please contact us.");
  - the second gives `400` `validation_failed` with `errors.purpose` "A signup token checks out as new.";
  - the third gives `404` `not_found`.
- Step 2: `502` `payment_provider_unavailable`.
- Step 3: `400` with the `Idempotency-Key` error.
- The organization stays Trialing.

With the sandbox keys, step 2 gives `201` with an `approvalUrl` at PayPal instead. The full PayPal journeys J2 to J6 are tested through the demo website: T3-5 to T3-9 **(sandbox)**.

## TA-11 Too many requests

**Use case:** someone floods the sign-up endpoints; the API must slow them down.

1. Send 11 billing-link requests at once:
   ```powershell
   1..11 | ForEach-Object {
     $x = Invoke-WebRequest -Method Post -Uri "$api/billing-access/requests" -Body '{"email":"nobody@example.org","captchaToken":"test"}' -ContentType "application/json" -SkipHttpErrorCheck
     "$_  $($x.StatusCode)  Retry-After: $($x.Headers['Retry-After'])"
   }
   ```
2. Wait a minute and run `Call GET /health`.

**Expected:**
- Step 1: the first calls give `202`. From the 11th at the latest, the calls give `429` with a `Retry-After` of up to 60 seconds. Earlier writes in the same minute count too, so the `429`s can start sooner.
- Step 2: `200`.
- Reads such as `/plans` have their own, higher limit (60 a minute).

## TA-12 Only the website may call from a browser (CORS)

**Use case:** another website must not be able to use the API from its visitors' browsers.

1. Ask as a browser on another website would:
   ```powershell
   $x = Invoke-WebRequest -Method Options -Uri "$api/plans" -Headers @{ Origin = "https://evil.example"; "Access-Control-Request-Method" = "GET" } -SkipHttpErrorCheck
   "status $($x.StatusCode), allowed origin: [$($x.Headers['Access-Control-Allow-Origin'])]"
   ```

**Expected:** `status 204, allowed origin: []`. With no allowed origin, the browser blocks the other website. Locally no website origin is allowed at all (`Site:PublicWebsiteOrigins` is empty). On staging, the web team's origin is allowed, and the same question from it gets that origin back. The automated tests check that.

## TA-13 A back-office sign-in gives nothing

**Use case:** a signed-in admin's browser must not open the API without a token.

1. In the browser, sign in to the app as any organization admin.
2. In the same window, open `https://localhost:7078/api/public/v1/account/subscription`.

**Expected:** the page shows JSON with `"status": 401` and `"code": "token_missing"`. The back-office sign-in is ignored.

---

## Known issues

| Test | What you see | Status |
|---|---|---|
| TA-9 | `trialEndsAt` and `currentPeriodEnd` have no `Z` at the end, unlike `signupTokenExpiresAt` and `expiresAt`. They are UTC times too | Found 2026-10-21; small, to tidy later |

## Results

| Test | What it checks | Result | Notes |
|---|---|---|---|
| TA-1 | Public reads | | |
| TA-2 | Sign up for a free trial (J1) | | |
| TA-3 | The same request twice (Idempotency-Key) | | |
| TA-4 | Mistakes in a sign-up | | |
| TA-5 | The signup token | | |
| TA-6 | Send the confirmation email again | | |
| TA-7 | The owner confirms the email | | |
| TA-8 | A manage-subscription link (J3) | | |
| TA-9 | The organization's subscription and invoices | | |
| TA-10 | Checkouts without PayPal | | |
| TA-11 | Too many requests | | |
| TA-12 | Only the website may call from a browser (CORS) | | |
| TA-13 | A back-office sign-in gives nothing | | |
