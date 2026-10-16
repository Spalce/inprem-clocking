# Website API: integration guide for the web team

How the marketing website registers organizations and manages their subscriptions through the
Inprem Clock-in **website API**. It covers journeys **J1 to J6**, the rules every call follows, error
codes, limits and what the website must never do.

- **Base URL:** `https://app.inpremclockin.org/api/public/v1` (production); staging: see [Staging](#staging).
- **Machine-readable description:** generated from the code at `/api/public/docs` on staging; the agreed version is [`public-v1.draft.yaml`](public-v1.draft.yaml). A contract test keeps the two in step.
- **Working example:** the demo website in the app (`/demo-site`, staging only) uses exactly these calls; its source is `InpremClockingApp/wwwroot/demo-site`.

The website never sees passwords, card details or PayPal credentials. Owners choose their password in the app; payments happen at PayPal.

---

## 1. Rules for every call

### Authorization

| Credential | How to send | What it allows | Lifetime |
|---|---|---|---|
| none | — | Public endpoints (plans, time zones, slug check, health, signup, billing-access requests and sessions) | — |
| **signup token** | `Authorization: Bearer <signupToken>` | That signup and the checkouts it started | 2 hours |
| **billing-access token** | `Authorization: Bearer <billingAccessToken>` | Billing of one organization | 30 minutes |
| `X-Api-Key` (optional) | header, from the website's **server** only | A higher rate limit; never a substitute for the tokens | until rotated |

- **Tokens are opaque.** Do not parse them. They appear once, in the response that creates them.
- **Cookies are ignored.** A browser signed in to the back office gets nothing from this API without a token.
- **Errors:** an expired token gives `401 token_expired`; a missing one `token_missing`; an unknown one, or one used where it does not belong, `token_invalid`. Another signup's or organization's resources answer `404 not_found`.

### CORS

Only the website's origins are allowed (`https://inpremclockin.org`, `https://www.inpremclockin.org`, and the staging origin), for `GET` and `POST`, with the headers `Content-Type`, `Authorization`, `Idempotency-Key` and `X-Api-Key`. Requests are made **without credentials**; `Retry-After` is readable by the page.

### Idempotency

`POST /signups` and `POST /checkout-sessions` require an `Idempotency-Key` header: 8 to 100 characters, **one per user action** (a UUID made when the page is shown or the button pressed).

| You send | You get |
|---|---|
| The same key and the same body within 24 hours | The first answer again; nothing is created twice (also when both arrive at once) |
| The same key with a different body | `422 idempotency_key_reused` |
| The same key after a `5xx` (e.g. `502 payment_provider_unavailable`) | A fresh attempt: server errors are not kept, so retrying is safe |

### Rate limits

| Who | Limit per minute (defaults) |
|---|---|
| Per client address, no key | 60 requests |
| Per token | 120 requests |
| Per valid `X-Api-Key` | 600 requests |
| Write endpoints per address (signup, billing-access request and session) | 10 requests |

Over the limit: `429 rate_limited` with `Retry-After: <seconds>`. Wait that long before trying again.

### Errors

Every error is `application/problem+json` with a stable **`code`**: show your own text for it, and log `traceId` when you contact Inprem.

```json
{
  "type": "https://app.inpremclockin.org/problems/validation_failed",
  "title": "One or more fields are invalid.",
  "status": 400,
  "instance": "/api/public/v1/signups",
  "code": "validation_failed",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": { "slug": ["Use lower-case letters, digits and single dashes (e.g. akim-asafo)."], "owner.email": ["Give a valid email address (up to 256 characters)."] }
}
```

| Code | Status | Meaning |
|---|---|---|
| `validation_failed` | 400 | A field is wrong; `errors` is keyed by the JSON path (`owner.email`, `consent.acceptTerms`, …) |
| `captcha_failed` | 400 | The CAPTCHA answer was not accepted: show the widget again |
| `return_url_not_allowed` | 400 | `returnUrl` or `cancelUrl` is not https on an allowed website origin |
| `plan_not_available` | 400 | The price is not on sale (or not available for online payment), or the plan has no trial |
| `invalid_code` | 400 | The billing link's code is wrong, used or expired: offer to send a new link |
| `token_missing`, `token_invalid`, `token_expired` | 401 | See Authorization |
| `not_found` | 404 | Not there, or not visible to this token |
| `slug_taken` | 409 | The web name is taken: use `GET /slugs/{slug}/availability` for a suggestion |
| `email_in_use` | 409 | The owner's email already has an account: ask them to sign in to the app, or use another email |
| `checkout_final` | 409 | The checkout has already ended |
| `subscription_not_cancellable` | 409 | Nothing to cancel (not a PayPal subscription, or already cancelled) |
| `cannot_keep` | 409 | A cancellation cannot be withdrawn: offer "Subscribe again" (checkout with `resubscribe`) |
| `resend_limit_reached` | 409 | At most 3 verification emails per hour |
| `already_verified` | 409 | The email is already verified |
| `idempotency_key_reused` | 422 | See Idempotency |
| `rate_limited` | 429 | See Rate limits |
| `internal_error` | 500 | Unexpected; try again later |
| `payment_provider_unavailable` | 502 | PayPal could not be reached; retry with the same `Idempotency-Key` |

---

## 2. Public reads

```http
GET /plans
```

```json
[
  {
    "planId": "6f1c…",
    "name": "Standard",
    "description": "For most centres",
    "trialDays": 30,
    "features": ["Kiosk", "Reports"],
    "limits": { "maxPeople": 100 },
    "prices": [
      { "planPriceId": "9a2e…", "billingCycle": "Monthly", "price": { "amount": "29.00", "currency": "USD" } },
      { "planPriceId": "b71d…", "billingCycle": "Yearly", "price": { "amount": "290.00", "currency": "USD" } }
    ]
  }
]
```

Cached for 5 minutes (`Cache-Control: public, max-age=300`). Send only the `planPriceId` you are given; prices come from Inprem's catalogue, never from the website.

- `GET /timezones` → `[{ "id": "America/New_York", "displayName": "America/New_York (UTC-05:00)" }, …]` (IANA ids).
- `GET /slugs/{slug}/availability` → `{ "slug": "akim-asafo", "available": false, "suggestion": "akim-asafo-2" }`. A badly formed name gives `400 validation_failed`.
- `GET /health` → `{ "status": "ok" }`.

No public answer contains personal data.

---

## 3. Journeys

### J1: register with a free trial

```http
POST /signups
Idempotency-Key: 6e0f2a3c-1b4d-4c8e-9f20-7a5b3c1d2e4f
Content-Type: application/json

{
  "organizationName": "Akim Asafo Community Centre",
  "slug": "akim-asafo",
  "timeZone": "Africa/Accra",
  "owner": { "firstName": "Ama", "lastName": "Owusu", "email": "ama@example.org", "mobile": "+233201234567" },
  "consent": { "acceptTerms": true, "acceptPrivacyPolicy": true, "smsOptIn": false },
  "planPriceId": "9a2e…",
  "mode": "trial",
  "captchaToken": "<Turnstile answer>"
}
```

```http
201 Created
Location: /api/public/v1/signups/3d5c…

{ "signupId": "3d5c…", "signupToken": "…", "signupTokenExpiresAt": "2026-10-16T12:00:00Z", "status": "PendingVerification", "nextAction": "verify_email" }
```

1. Show "Check your email". The owner gets a link to the **app**, where they confirm the email and choose a password. Only then can anyone sign in (decision D22).
2. Optionally poll `GET /signups/{signupId}` with the signup token: `{ "signupId": "3d5c…", "status": "PendingVerification" | "Verified" | "Active", "subscriptionStatus": "Trialing" }`.
3. "Send the email again": `POST /signups/{signupId}/resend-verification` → `202` (at most 3 per hour).

The organization is now **Trialing**; no payment is taken.

### J2: register and subscribe now

1. The same `POST /signups` with `"mode": "subscribe"` → `"nextAction": "checkout"`. The organization has **no access** until PayPal confirms.
2. Start the checkout with the signup token:

```http
POST /checkout-sessions
Authorization: Bearer <signupToken>
Idempotency-Key: 0c9b…
Content-Type: application/json

{ "planPriceId": "9a2e…", "purpose": "new", "returnUrl": "https://www.inpremclockin.org/subscribe/return", "cancelUrl": "https://www.inpremclockin.org/subscribe/cancelled" }
```

```json
201 Created
{ "checkoutSessionId": "c41e…", "approvalUrl": "https://www.paypal.com/webapps/billing/subscriptions?ba_token=…", "expiresAt": "2026-10-15T15:00:00Z" }
```

3. Keep `checkoutSessionId` and the token in **sessionStorage**, then send the browser to `approvalUrl`.
4. PayPal sends the buyer back to `returnUrl` (PayPal adds its own query parameters; ignore them). Show "Confirming your subscription…" and call:

```http
POST /checkout-sessions/c41e…/confirm
Authorization: Bearer <signupToken>
```

```json
{
  "checkoutSessionId": "c41e…",
  "status": "Approved",
  "subscriptionStatus": "PendingActivation",
  "plan": { "planId": "6f1c…", "planPriceId": "9a2e…", "name": "Standard", "billingCycle": "Monthly", "price": { "amount": "29.00", "currency": "USD" } },
  "nextBillingDate": null,
  "message": "Confirming your subscription…"
}
```

5. **Poll** `GET /checkout-sessions/{id}` until the status is final (see the [polling rules](#polling-rules)). On `Active`, show success: "Check your email to set your password".

**Never show success because PayPal sent the buyer back.** A subscription is Active only when this API says so; PayPal's redirect alone proves nothing (design 7.5).

### J3: convert a trial to paid, and J4: manage a subscription

Owners have no password on the website. They get a one-time link:

1. "Manage my subscription": `POST /billing-access/requests` with `{ "email": "ama@example.org", "captchaToken": "…" }`.
   - The answer is **always** `202 { "message": "If this email belongs to an organization owner, we have sent a link to manage the subscription." }`, in the same time, whoever asks. Show the message as it is.
2. Owners of an organization receive a link to `https://<website>/billing?code=…`. It works **once, for 15 minutes**.
3. On that page, exchange the code at once, then remove it from the address bar:

```http
POST /billing-access/sessions
{ "code": "…" }
```

```json
201 Created
{ "billingAccessToken": "…", "expiresAt": "2026-10-15T12:30:00Z", "organizationName": "Akim Asafo Community Centre" }
```

4. With the billing-access token:
   - `GET /account/subscription` → `{ "status": "Trialing", "plan": { … }, "billingMode": "PayPal", "trialEndsAt": "…", "currentPeriodEnd": null, "cancelAtPeriodEnd": false, "outstandingBalance": { "amount": "0.00", "currency": "USD" } }`.
   - **Pay** (J3, convert a trial), **subscribe again** (after cancellation, expiry or suspension) or **change plan** (J4): `POST /checkout-sessions` with `purpose` = `convert`, `resubscribe` or `change_plan`, then the same PayPal, confirm and poll steps as J2.
   - **Cancel:** `POST /account/subscription/cancel` → `{ "status": "Canceled", "cancelAtPeriodEnd": true, "accessEndsAt": "2026-11-15T00:00:00Z" }`. The organization keeps full access until then.
   - `POST /account/subscription/keep` answers `409 cannot_keep`: PayPal cannot reactivate a cancelled subscription. Offer "Subscribe again" instead.
   - `GET /account/invoices` → the organization's invoices, newest first (drafts are never shown):
     `[{ "invoiceId": "…", "number": "INV-100012", "status": "Sent", "total": { "amount": "70.00", "currency": "USD" }, "amountDue": { "amount": "70.00", "currency": "USD" }, "amountRefunded": null, "issuedAt": "2026-10-16T00:00:00Z", "dueDate": "2026-10-30", "periodStart": null, "periodEnd": null, "paymentUrl": "https://www.paypal.com/invoice/p/#…" }]`.
     - `status`: `Sent`, `PartiallyPaid`, `Overdue` (still to pay after the due date), `Paid`, `Refunded`, `Cancelled`.
     - Show a **Pay** button only when `paymentUrl` is set (a PayPal invoice with something still to pay). An invoice paid offline has no link: ask the payer to quote the `number`.

Which `purpose` to use, from `status`:

| `status` | Offer | `purpose` |
|---|---|---|
| `Trialing` | Pay | `convert` |
| `Expired`, `Canceled`, `Suspended` | Subscribe again | `resubscribe` |
| `Active`, `PastDue` with `billingMode` `PayPal` | Change plan, Cancel | `change_plan` |
| anything else (e.g. `Free`, `Invoiced`) | Contact Inprem | — |

The same can be done in the app by the organization's owner (Billing page).

### J5: abandoned or cancelled checkout

The buyer presses "Cancel" at PayPal and arrives at `cancelUrl`. Call `POST /checkout-sessions/{id}/cancel` → the status with `"status": "Cancelled"`. Show "Checkout was not completed" with "Try again". A checkout not approved within 3 hours becomes `Expired`. Either way the organization stays as it was.

### J6: payment problem

PayPal declines, or the subscription never becomes active: the checkout ends `Failed` (`"message": "We could not activate your subscription. Please contact us."`). The organization is not activated, and Inprem is alerted automatically. Show the message and a support contact; do not retry automatically.

### Polling rules

After `/confirm` while the status is `Approved` (or `AwaitingApproval`):

1. `GET /checkout-sessions/{id}` every **2 seconds for 30 seconds**, then every **10 seconds up to 2 minutes**.
2. **Stop** on `Active`, `Cancelled`, `Expired` or `Failed`.
3. Still `Approved` after 2 minutes: show "Payment received, activation is taking longer than usual. We will email you." and stop. Inprem's reconciliation finishes it.

| Status | Show |
|---|---|
| `Created`, `AwaitingApproval` | A button to continue to PayPal |
| `Approved` | "Confirming your subscription…" (keep polling) |
| `Active` | Success; "check your email to set your password" (new owners) or "go to the app" |
| `Cancelled`, `Expired` | "Checkout was not completed" and Try again |
| `Failed` | "We could not activate your subscription" and a support contact |

---

## 4. What the website must never do

- **Never treat the PayPal return redirect as payment.** Only the API's `Active` counts.
- **Never keep tokens beyond the session:** use `sessionStorage` (or memory), never `localStorage`, cookies or URLs. Remove the billing `code` from the address bar once it is exchanged.
- **Never send, ask for or store passwords**, card numbers or PayPal credentials. Owners set passwords in the app.
- **Never send prices or PayPal plan ids:** only Inprem's `planPriceId`.
- **Never put the `X-Api-Key` in the browser:** it is for the website's server only.
- **Never retry a `4xx` unchanged** (except `429` after `Retry-After`); retry a `502` with the same `Idempotency-Key`.
- **Never reuse an `Idempotency-Key` for a different action.**
- **Never tell a visitor whether an email is a customer:** show the billing-access message as it is.

---

## 5. CAPTCHA

Cloudflare **Turnstile** (decision D30) protects `POST /signups` and `POST /billing-access/requests`. Show the widget with Inprem's **site key** and send its answer as `captchaToken`; Inprem's server checks it with Cloudflare. For development, Cloudflare's test site key `1x00000000000000000000AA` always passes (with the matching test secret on Inprem's side).

---

## 6. PayPal sandbox

Staging uses the PayPal **sandbox**: no real money.

- Buy with a sandbox **personal (buyer)** account from the PayPal developer dashboard (Testing Tools → Sandbox Accounts), or a test card the sandbox offers.
- To see a decline (J6), use the sandbox's negative testing or a card set up to fail.
- The return and cancel URLs must be on Inprem's allow-list: tell Inprem every origin you use (production, staging, local).

---

## 7. Staging

Ask Inprem for the values marked *(given by Inprem)*.

| Item | Value |
|---|---|
| Base URL | `https://staging-app.inpremclockin.org/api/public/v1` (hostname confirmed at the domain cutover) |
| OpenAPI document | `https://staging-app.inpremclockin.org/api/public/docs` |
| Demo website (working example) | `https://staging-app.inpremclockin.org/demo-site/` |
| Allowed website origins (CORS and return URLs) | Your staging origin *(send it to Inprem)* |
| Turnstile site key | *(given by Inprem)*; or the test key above |
| Billing link target | `https://<your staging site>/billing?code=…` *(Inprem sets this address)* |
| `X-Api-Key` for your server | *(given by Inprem, privately)* |
| Support | Include the `traceId` of the error |

Emails on staging (verification and billing links) go to the addresses you register; use addresses you can read.

---

## 8. Changes from the draft

The agreed draft ([`public-v1.draft.yaml`](public-v1.draft.yaml)) was written before the code. Building it changed four things, to confirm with the web team:

- `internal_error` was added to the problem codes (the draft had none for a 500).
- `limits.maxPeople` (staff and volunteers together) was added to plans; the draft's separate staff, volunteer and admin limits wait for the plan catalogue decision.
- `POST /account/subscription/keep` always answers `409 cannot_keep`, because PayPal cancellations are final. An "undo" would need cancellation scheduled at the period end instead.
- Invoices have an optional `amountRefunded` (Phase 4): a partly refunded invoice stays `Paid` with the amount given back. `Void` and `Draft` from the draft's status list are never returned (drafts are not shown).
