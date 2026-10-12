# 05 — Phase 3: Website API (registration and subscriptions)

**Entry:** Phase 2 exit checks pass. Decisions: D10, D19–D22, D27, D29, D30. CAPTCHA test keys via user-secrets (Turnstile provides test keys).
**Branch:** `mt/phase3` from `mt/integration`.
**Design references:** sections 2.3, 7, 13 (Phase 3). Draft spec: `docs/multitenancy/api/public-v1.draft.yaml` (from G0).
**Site:** App site only. Base URL `https://app.inpremclockin.org/api/public/v1`.

Follow `00-guardrails.md`. Run all required suites after every step; update `PROGRESS.md`.

## Step 3.1 — API skeleton

- Minimal API endpoint group (or controllers) under `/api/public/v1`, mapped only on the App site.
- Authentication schemes for the API **only**: `SignupToken` and `BillingAccessToken` (opaque random 32-byte tokens, stored as SHA-256 hashes, `Authorization: Bearer`), optional `X-Api-Key` for the website's server. The API must **ignore the back-office cookie** (explicit scheme selection on the group; test it).
- CORS policy: only origins in `Site:PublicWebsiteOrigins` (`https://inpremclockin.org`, `https://www.inpremclockin.org`, staging origin). No credentials mode.
- Rate limiting (built-in ASP.NET Core rate limiter): per IP and per token; `429` with `Retry-After`.
- `ProblemDetails` for all errors with stable `code` values (`validation_failed`, `slug_taken`, `captcha_failed`, `token_expired`, `checkout_expired`, ...).
- Idempotency middleware for endpoints that accept `Idempotency-Key`: store key + request hash + response for 24 hours; same key + same body → same response; same key + different body → 422.
- OpenAPI generated from code (Swashbuckle) published at `/api/public/docs` on staging only; the generated document must match the agreed spec (contract test).

## Step 3.2 — Public read endpoints

`GET /plans`, `GET /timezones`, `GET /slugs/{slug}/availability` (with suggestion), `GET /health`. Cache plans for 5 minutes. No personal data.

## Step 3.3 — Registration

- `POST /signups` (CAPTCHA + Idempotency-Key): validates organization, slug, time zone, owner name/email/mobile, consent flags, `planPriceId`, `mode` (`trial`|`subscribe`). Creates tenant (PendingVerification), owner user without password, Owner membership, subscription (Trialing for trial; PendingActivation for subscribe), and a signup record. Returns `201 { signupId, signupToken, status, nextAction }`.
- `GET /signups/{id}`, `POST /signups/{id}/resend-verification` (max 3/hour), using `signupToken` (2-hour lifetime, scoped to that signup).
- Verification email links to the App site page that verifies the email and lets the owner set a password; access to the back office only after verification (D22).

## Step 3.4 — Checkout endpoints

- `POST /checkout-sessions` (signupToken or billingAccessToken + Idempotency-Key): body `planPriceId`, `purpose`, `returnUrl`, `cancelUrl`; calls the Phase 2 `StartCheckout` use case; returns `{ checkoutSessionId, approvalUrl, expiresAt }`.
- `GET /checkout-sessions/{id}`, `POST /checkout-sessions/{id}/confirm`, `POST /checkout-sessions/{id}/cancel` with the same token; responses include `status`, `subscriptionStatus`, `plan`, `nextBillingDate`, `message`.
- Tokens may only access checkout sessions they created.

## Step 3.5 — Existing customers (billing access)

- `POST /billing-access/requests` (CAPTCHA): always `202`; if the email belongs to an organization **Owner**, email a one-time code link (15 minutes) to the marketing website's billing page. Identical response time and body for unknown emails (add a small constant delay).
- `POST /billing-access/sessions`: exchange code (single use) for `billingAccessToken` (30 minutes, scoped to that organization's billing).
- `GET /account/subscription`, `GET /account/invoices`, `POST /account/subscription/cancel`, `POST /account/subscription/keep`; conversions, resubscribe and plan changes go through `POST /checkout-sessions` with the right `purpose`.

## Step 3.6 — Demo website page (D29)

Until the web team's site exists, add `wwwroot/demo-site/` (plain HTML + `fetch`, no build step), served only when `Demo:EnableDemoSite=true`: pricing → signup (trial or subscribe) → PayPal → `return.html` that calls confirm and polls status → result; and a "Manage subscription" page using billing-access links. Its origin is added to CORS when enabled. Playwright tests drive J1–J5 through it with a fake PayPal.

## Step 3.7 — Web team handover

- `docs/multitenancy/api/integration-guide.md`: journeys J1–J6 with request/response examples, status polling rules (2 s for 30 s, then 10 s up to 2 min), error codes, CORS, rate limits, idempotency, sandbox test accounts guidance, and what the website must never do (trust the return redirect alone, store tokens in local storage beyond the session, send passwords).
- Staging environment notes for the web team.

## Tests

- Contract tests: OpenAPI matches implementation; examples execute.
- CAPTCHA required; rate limit 429; billing-access responses identical for known/unknown emails (body and status; timing within tolerance).
- Idempotency: same key → one record.
- Token scope: signup and billing tokens cannot read or modify another organization (tenant-isolation suite extended); expired tokens rejected.
- Return/cancel URL outside the allow-list rejected.
- API rejects a request carrying only the back-office cookie (no token).
- CORS rejects other origins.
- Response-schema privacy check: public endpoints return no personal data.
- Load test (k6 or NBomber) on a test site: public reads p95 < 300 ms, signup p95 < 1 s at 20 rps (PayPal time excluded).

## Acceptance criteria

| ID | Criterion | Measure |
|---|---|---|
| P3-1 | Journeys | J1–J6 completed end-to-end through the demo website page (and later the web team's site) with the PayPal sandbox; recorded in `PROGRESS.md` |
| P3-2 | Contract | OpenAPI published; contract tests 100% |
| P3-3 | Abuse protection | CAPTCHA enforced; 429 on limits; no email enumeration |
| P3-4 | Idempotency | Repeated key creates exactly one record |
| P3-5 | Token scope | No cross-organization access; expired tokens rejected |
| P3-6 | Redirect safety | Non-allow-listed URLs rejected |
| P3-7 | Performance | p95 targets met on staging |
| P3-8 | Privacy | No personal data in public responses |
| P3-9 | Cookie independence | Cookie-only requests rejected; CORS limited to the website origins |

## Out of scope

The marketing website itself (web team), PayPal invoices (Phase 4), SMS (Phase 5), the root-domain switch (`09-domain-cutover.md`).
