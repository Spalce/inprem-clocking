# Website API: the docs page (Swagger)

[← All test guides](README.md) · [The same API from PowerShell](website-api.md)

The app publishes a page that describes the website API and lets you try it in the browser: `https://localhost:7078/api/public/docs`. The web team reads it to build their site, so it must be complete and correct. This guide checks the page itself and the calls it can make.

---

## Before you start

1. Start the app as in [Before you start](README.md#the-local-copy).
2. The page is on only where `PublicApi:EnableDocs` is `true`. Your local user-secrets have it. The live test site doesn't, and must not.

**Words on the page:**
- each endpoint is a coloured row: blue for **GET** (read), green for **POST** (do something);
- **Try it out** opens the boxes you can fill in;
- **Execute** sends the request from your browser;
- under **Responses**, **Server response** is what really came back. The list below it, under **Code** and **Description**, is only what the description *promises* can come back.

### Where `captchaToken` comes from

On a real website, the visitor's browser shows Cloudflare's Turnstile widget (the "I'm human" box). When the visitor passes it, the widget gives the page a one-time answer, and the website sends that answer as `captchaToken`. The app then asks Cloudflare whether the answer is genuine. The demo website (`/demo-site`) works this way, with Cloudflare's test site key `1x00000000000000000000AA`.

For direct calls there is no widget, and you don't need one. Your local `Captcha:Secret` is Cloudflare's test secret `1x0000000000000000000000000000000AA`, and Cloudflare accepts **any** answer checked with it. So any non-empty text works: TS-6 and TS-9 use `"test"`.

| Situation | Answer |
|---|---|
| `captchaToken` empty or missing | `400 validation_failed`, with `errors.captchaToken` "The CAPTCHA answer is required." |
| Your computer is offline, or can't reach Cloudflare | `400 captcha_failed`: the app still asks Cloudflare, even with the test secret |
| `Captcha:Secret` not set | `400 captcha_failed` |
| `Captcha:Secret` set to Cloudflare's always-fail test secret `2x0000000000000000000000000000000AA` | `400 captcha_failed` |

On staging and production a real secret is used. Made-up text then fails, and only an answer from the real widget passes: test there through the demo website or the web team's site.

At most **10 write calls a minute** are allowed from your computer (sign-up and billing-link calls together). Over it, the page shows `429` with `rate_limited`. Wait a minute.

---

## TS-1 The page opens and lists everything

**Use case:** the web team opens the page to see what the API offers.

1. Open `https://localhost:7078/api/public/docs`.
2. Look at the top, then scroll to the bottom.

**Expected:**
- The top:
  - the title **InpremClockin Website API**, with **1.0.0** and **OAS 3.0** next to it;
  - under it, the link `/api/public/docs/public-v1/openapi.json` (the description as a file; click it to see the raw JSON, then go **Back**);
  - an **Authorize** button with a padlock.
- Five groups, each a heading you can fold: **Account**, **Billing access**, **Checkout**, **Public** and **Signup**, with 17 rows in all:
  - **Public**: `GET /api/public/v1/health`, `/plans`, `/timezones`, `/slugs/{slug}/availability`;
  - **Signup**: `POST /signups`, `GET /signups/{signupId}`, `POST /signups/{signupId}/resend-verification`;
  - **Checkout**: `POST /checkout-sessions`, `GET /checkout-sessions/{checkoutSessionId}`, `POST …/confirm`, `POST …/cancel`;
  - **Billing access**: `POST /billing-access/requests`, `POST /billing-access/sessions`;
  - **Account**: `GET /account/subscription`, `GET /account/invoices`, `POST /account/subscription/cancel`, `POST /account/subscription/keep`.
- At the bottom, **Schemas**: the shapes of the requests and answers (TS-8).

## TS-2 Health

**Use case:** the web team checks the API is up before anything else.

1. In **Public**, click the row **GET /api/public/v1/health**. It opens.
2. Click **Try it out** (right side), then the blue **Execute** button.
   - **What it does:** sends the request from your browser and shows what came back.
3. Look under **Responses**.

**Expected:**
- **Curl**: the same request as a command line.
- **Request URL**: `https://localhost:7078/api/public/v1/health`.
- **Server response**:
  - **Code** `200`;
  - **Response body** `{ "status": "ok" }`;
  - **Response headers**.
- Under it, the promised answers: `200` and `429`.
- Click **Clear** to empty the answer. Clicking **Cancel** (where **Try it out** was) closes the boxes.

## TS-3 Plans

**Use case:** the website builds its pricing page.

1. Open **GET /api/public/v1/plans**, click **Try it out**, **Execute**.
2. Look at **Response body** and **Response headers**.
3. Copy one `planPriceId` value (between the quotes) into Notepad, for later.

**Expected:**
- Code `200`.
- The body lists the plans with **Show on the public pricing page** ticked (T1-7), each with:
  - `name`, `trialDays`, `features`, `limits.maxPeople`;
  - `prices`, where each price has `planPriceId`, `billingCycle` and `price` (`amount` like `"10.00"`, `currency` `"USD"`).
- The headers include `cache-control: public, max-age=300`.
- Hidden plans are not listed. Untick **Show on the public pricing page** for a plan in the console and repeat: it disappears within 5 minutes (the answer is kept for 5 minutes).

## TS-4 Time zones

1. Open **GET /api/public/v1/timezones**, **Try it out**, **Execute**.

**Expected:** `200` and a list of `id` (e.g. `Africa/Accra`) and `displayName` (e.g. `Africa/Accra (UTC+00:00)`). These are the same time zones as the console's **New organization** wizard.

## TS-5 Is a web name free?

**Use case:** while the visitor types a web name, the website says whether it is free.

1. Open **GET /api/public/v1/slugs/{slug}/availability**, click **Try it out**.
2. In the **slug** box (marked *required*), type `my-new-centre`, click **Execute**.
3. Repeat with `admin`, then with the web name of an organization that exists (e.g. one from T3-2), then with `Bad Name`.

**Expected:**
- `my-new-centre`: `200` with `"available": true` and `"suggestion": null`.
- `admin`: `200` with `"available": false` and `"suggestion": "admin-org"` (reserved names are never free).
- An existing name: `"available": false` with a free suggestion, such as `…-2`.
- `Bad Name`: `400` with `"code": "validation_failed"` and `errors.slug` "Use lower-case letters, digits and single dashes (e.g. akim-asafo)."

## TS-6 Ask for a manage-subscription link

**Use case:** an existing customer asks the website for a link to manage their subscription.

1. Open **POST /api/public/v1/billing-access/requests**, click **Try it out**.
2. **Request body** shows an example with `"string"` in each value. Replace it with the owner email from T3-2 or TA-2:
   ```json
   { "email": "<the owner's email>", "captchaToken": "test" }
   ```
   Click **Execute**.
   - **What it does:** if the email is an organization owner's, a background job emails a one-time link, valid for 15 minutes. The answer is the same either way.
3. Repeat with an email that is not a customer, e.g. `nobody@example.org`.
4. Repeat with `"email": "nope"`.
5. Open the drop folder.

**Expected:**
- Steps 2 and 3: the same `202` and the same body: "If this email belongs to an organization owner, we have sent a link to manage the subscription."
- Step 4: `400` `validation_failed` with `errors.email`.
- Step 5: exactly one new **Manage your Inprem Clock-in subscription** email, for the owner.

## TS-7 Exchange the link's code

**Use case:** the website turns the link's code into a billing-access token.

1. In the TS-6 email, **don't click the button**: clicking opens the demo website, which uses the code at once. Right-click it → **Copy link address**, paste it into Notepad, and copy the part after `code=`.
2. Open **POST /api/public/v1/billing-access/sessions**, **Try it out**, and set **Request body** to:
   ```json
   { "code": "<the code>" }
   ```
   Click **Execute**.
3. Click **Execute** again with the same code.

**Expected:**
- Step 2: `201` with `billingAccessToken`, `expiresAt` (30 minutes from now) and `organizationName`.
- Step 3: `400` `invalid_code` ("This link is not valid, was already used or has expired. Ask for a new one.").

Keep the token for TS-9 (it is valid for 30 minutes).

## TS-8 The descriptions are right

**Use case:** the web team builds from these descriptions, so they must match what the API does.

1. Open **POST /api/public/v1/signups**. Under **Request body**, click **Schema** (next to **Example Value**).
2. Under **Responses**, read the promised codes.
3. At the bottom, open **Schemas** and click **SignupRequest**, **SignupCreatedResponse**, **PlanResponse** and **ProblemDetails**.
4. Compare with the [integration guide](../api/integration-guide.md), sections 2 and 3.

**Expected:**
- **SignupRequest** has:
  - `organizationName`, `slug`, `timeZone`;
  - `owner` (`firstName`, `lastName`, `email`, `mobile`);
  - `consent` (`acceptTerms`, `acceptPrivacyPolicy`, `smsOptIn`);
  - `planPriceId`, `mode`, `captchaToken`.
- The promised codes for sign-up are `201`, `400`, `409`, `422` and `429`.
- **SignupCreatedResponse** has `signupId`, `signupToken`, `signupTokenExpiresAt`, `status` and `nextAction`.
- **PlanResponse** matches what TS-3 returned.
- Every field the integration guide names is in a schema, with the same name.
- **ProblemDetails** shows only `type`, `title`, `status`, `detail` and `instance`. `code`, `traceId` and `errors` are missing from it. They are extra fields on every error. Note it, not a fail.

## TS-9 Sign up and use the tokens from the page

**Use case:** the web team tries a whole sign-up from the docs page, then reads it back with the token.

1. Open **POST /api/public/v1/signups**, click **Try it out**.
   - **What it does:** under **Parameters**, a box **Idempotency-Key** (marked *required*, *header*) appears. The website makes one key per click of **Register**.
2. In **Idempotency-Key**, type `swagger-test-0001`. Replace **Request body** with this, using the `planPriceId` from TS-3 and a web name and email used nowhere else:
   ```json
   {
     "organizationName": "Swagger Test Centre",
     "slug": "swagger-test-1",
     "timeZone": "Africa/Accra",
     "owner": { "firstName": "Ama", "lastName": "Owusu", "email": "swagger1@example.org", "mobile": "" },
     "consent": { "acceptTerms": true, "acceptPrivacyPolicy": true, "smsOptIn": false },
     "planPriceId": "<from TS-3>",
     "mode": "trial",
     "captchaToken": "test"
   }
   ```
   Click **Execute**. Copy `signupId` and `signupToken` from **Response body** into Notepad.
3. Click **Execute** again, unchanged. Then change `slug` to `swagger-test-2`, keep the key, and **Execute**.
4. Click **Authorize** (top). Paste the signup token into **signupToken** → **Authorize** → **Close**.
5. Open **GET /api/public/v1/signups/{signupId}**, **Try it out**, type the `signupId`, **Execute**.
6. Click **Authorize**. Under **signupToken**, click **Logout**. Paste the billing-access token from TS-7 into **billingAccessToken** → **Authorize** → **Close**.
7. Open **GET /api/public/v1/account/subscription**, **Try it out**, **Execute**.

**Expected:**
- Step 1: the **Idempotency-Key** box is there. Rows that need a token show a padlock on the right: the **Signup** rows other than `POST /signups`, and every **Checkout** and **Account** row.
- Step 2: `201`, with `"status": "PendingVerification"` and `"nextAction": "verify_email"`. **Curl** includes `-H 'Idempotency-Key: swagger-test-0001'`. The drop folder has **Confirm your email for Swagger Test Centre on Inprem Clock-in**.
- Step 3: the first **Execute** gives exactly the same answer (nothing new is created). The changed one gives `422` `idempotency_key_reused`.
- Step 5: `200`, with `"status": "PendingVerification"`. **Curl** has an `Authorization: Bearer …` line.
- Step 7: `200`, the subscription of the organization from TS-7. With the signup token instead, it gives `401` `token_invalid`.
- Without **Authorize**, the same calls give `401` `token_missing`.

## TS-10 The page is off unless switched on

**Use case:** the live site must not publish the page.

1. Stop the app (Ctrl+C in its terminal). Run:
   ```powershell
   dotnet user-secrets set PublicApi:EnableDocs false --project InpremClockingApp
   ```
   Start the app again.
2. Open `https://localhost:7078/api/public/docs`, then `https://localhost:7078/api/public/v1/health`.
3. Switch it back on, and restart the app:
   ```powershell
   dotnet user-secrets set PublicApi:EnableDocs true --project InpremClockingApp
   ```

**Expected:** in step 2, the docs page is a "not found" page, while the API itself still answers `{ "status": "ok" }`. After step 3 the page is back.

---

## Known issues

| Test | What you see | Status |
|---|---|---|
| TS-8 | **ProblemDetails** doesn't show `code`, `traceId` and `errors`, which every error has (the agreed `public-v1.draft.yaml` does) | Found 2026-10-21; small |

## Results

| Test | What it checks | Result | Notes |
|---|---|---|---|
| TS-1 | The page opens and lists everything | | |
| TS-2 | Health | | |
| TS-3 | Plans | | |
| TS-4 | Time zones | | |
| TS-5 | Is a web name free? | | |
| TS-6 | Ask for a manage-subscription link | | |
| TS-7 | Exchange the link's code | | |
| TS-8 | The descriptions are right | | |
| TS-9 | Sign up and use the tokens from the page | | |
| TS-10 | The page is off unless switched on | | |
