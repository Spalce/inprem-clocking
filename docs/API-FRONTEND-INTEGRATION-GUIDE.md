# Inprem Clock-in API Frontend Integration Guide

## Audience

This guide is for the frontend developers integrating the public website registration flow with the Inprem Clock-in API.

The API is a REST-style HTTP API. The frontend should call it over HTTPS and should not access the database or Provider Console directly.

## API base URL

For the current deployed environment:

```text
https://inprem-003-site3.etempurl.com/api/public/v1
```

Use a separate environment variable for each deployment:

```text
Development: https://localhost:{port}/api/public/v1
QA/Staging: https://staging-domain.com/api/public/v1
Production: https://production-domain.com/api/public/v1
```

Do not hard-code the production URL into frontend source code.

## Main registration flow

The normal tenant-registration flow is:

1. Load available plans with `GET /plans`.
2. Load time zones with `GET /timezones`.
3. Check the requested organization slug with `GET /slugs/{slug}/availability`.
4. Render the Cloudflare Turnstile CAPTCHA widget.
5. Submit the registration with `POST /signups`.
6. Save the returned signup ID and temporary signup token only for the current registration flow.
7. Tell the owner to check their email.
8. Use the signup token to check status or resend the verification email.
9. The owner opens the verification link in the email and chooses a password.
10. If the registration requires paid checkout, create a checkout session using the signup token.

## Public endpoints

### Health check

```text
GET {API_BASE_URL}/health
```

Example response:

```json
{
  "status": "ok"
}
```

### Get available plans

```text
GET {API_BASE_URL}/plans
```

Use an active public plan price from this response as `planPriceId`. Do not create or guess a plan price ID in the frontend.

Example response shape:

```json
[
  {
	"id": "PLAN-ID",
	"name": "Community",
	"description": "Community plan",
	"trialDays": 30,
	"features": ["Feature one", "Feature two"],
	"limits": {
	  "maxPeople": 100
	},
	"prices": [
	  {
		"planPriceId": "PLAN-PRICE-ID",
		"billingCycle": "Monthly",
		"price": {
		  "amount": "15.00",
		  "currency": "USD"
		}
	  }
	]
  }
]
```

### Get time zones

```text
GET {API_BASE_URL}/timezones
```

Use the returned time-zone ID as the value of `timeZone` in the signup request.

Example response shape:

```json
[
  {
	"id": "America/New_York",
	"displayName": "Eastern Time"
  }
]
```

### Check slug availability

```text
GET {API_BASE_URL}/slugs/{slug}/availability
```

Example:

```text
GET https://inprem-003-site3.etempurl.com/api/public/v1/slugs/example-center/availability
```

Example response:

```json
{
  "slug": "example-center",
  "available": true,
  "suggestion": null
}
```

If the slug is unavailable, the response may contain a suggested alternative.

## CAPTCHA integration

### Important rule

The frontend must not hard-code, store, or reuse a CAPTCHA token. A Turnstile token is intentionally short-lived and should be generated for the current form submission.

The API validates the token server-side. The frontend only sends the token it receives from the Turnstile widget.

### What the frontend developer needs

The backend/deployment owner must provide the frontend developer with:

- The Turnstile **site key** for the frontend environment.
- The API base URL for that same environment.
- The configured allowed website origin.

Never give the frontend developer the Turnstile secret. The secret stays in the server configuration.

### Browser integration example

Load the Cloudflare Turnstile script once on the signup page:

```html
<script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
```

Render the widget in the form. Replace the site key with the key supplied for the current environment:

```html
<div
  class="cf-turnstile"
  data-sitekey="YOUR_TURNSTILE_SITE_KEY"
  data-callback="onTurnstileSuccess"
  data-expired-callback="onTurnstileExpired"
  data-error-callback="onTurnstileError">
</div>
```

Frontend example:

```javascript
let captchaToken = null;

function onTurnstileSuccess(token) {
  captchaToken = token;
}

function onTurnstileExpired() {
  captchaToken = null;
}

function onTurnstileError() {
  captchaToken = null;
}
```

Before submitting the signup form:

```javascript
if (!captchaToken) {
  throw new Error("Please complete the CAPTCHA challenge.");
}
```

The frontend then sends the current value in the JSON property `captchaToken`.

### How to test CAPTCHA

For local or QA testing, use the Turnstile test site key supplied by the deployment owner. The browser widget generates the token automatically.

The current demo site is an example of this flow:

```text
https://inprem-003-site3.etempurl.com/demo-site/
```

If the demo site is enabled, open its signup page and complete the form. It obtains the CAPTCHA token from the widget and sends it to the API.

Postman does not generate a Turnstile token. For Postman testing, ask the backend/deployment owner for a QA-only test-token configuration. Do not copy a token from an old request and do not use production CAPTCHA secrets in the browser.

If the API returns `captcha_failed`, the token was missing, expired, invalid, or created for a different environment.

## Create a tenant signup

### Request

```text
POST {API_BASE_URL}/signups
```

Required headers:

```text
Content-Type: application/json
Idempotency-Key: UNIQUE-REQUEST-ID
```

The `Idempotency-Key` should be a new random value for each new user action. If the frontend retries the exact same request because of a network failure, it should reuse the same key and request body.

Example request body:

```json
{
  "organizationName": "Example Community Center",
  "slug": "example-community-center",
  "timeZone": "America/New_York",
  "owner": {
	"firstName": "Ama",
	"lastName": "Owusu",
	"email": "owner@example.com",
	"mobile": "+15551234567"
  },
  "consent": {
	"acceptTerms": true,
	"acceptPrivacyPolicy": true,
	"smsOptIn": false
  },
  "planPriceId": "PLAN-PRICE-ID-FROM-PLANS",
  "mode": "trial",
  "captchaToken": "CURRENT-TURNSTILE-TOKEN"
}
```

The values for `planPriceId`, `timeZone`, and `captchaToken` must come from the current environment or current user interaction.

### Successful response

Status: `201 Created`

```json
{
  "signupId": "SIGNUP-ID",
  "signupToken": "TEMPORARY-SIGNUP-TOKEN",
  "signupTokenExpiresAt": "2026-01-01T12:00:00Z",
  "status": "PendingVerification",
  "nextAction": "verify_email"
}
```

The API creates the organization and owner account, but the owner must verify their email and choose a password before normal sign-in is available.

### Frontend JavaScript example

```javascript
const API_BASE_URL = "https://inprem-003-site3.etempurl.com/api/public/v1";

async function createSignup(formValues, captchaToken) {
  const response = await fetch(`${API_BASE_URL}/signups`, {
	method: "POST",
	headers: {
	  "Content-Type": "application/json",
	  "Idempotency-Key": crypto.randomUUID()
	},
	body: JSON.stringify({
	  organizationName: formValues.organizationName,
	  slug: formValues.slug,
	  timeZone: formValues.timeZone,
	  owner: {
		firstName: formValues.firstName,
		lastName: formValues.lastName,
		email: formValues.email,
		mobile: formValues.mobile
	  },
	  consent: {
		acceptTerms: formValues.acceptTerms,
		acceptPrivacyPolicy: formValues.acceptPrivacyPolicy,
		smsOptIn: formValues.smsOptIn
	  },
	  planPriceId: formValues.planPriceId,
	  mode: formValues.mode,
	  captchaToken
	})
  });

  const data = await response.json();

  if (!response.ok) {
	throw { status: response.status, problem: data };
  }

  return data;
}
```

The frontend should display a success message such as:

> Registration received. Check your email to confirm your address and create your password.

## Signup status

Use the token returned by `POST /signups`.

```text
GET {API_BASE_URL}/signups/{signupId}
```

Header:

```text
Authorization: Bearer SIGNUP-TOKEN
```

Example response:

```json
{
  "signupId": "SIGNUP-ID",
  "status": "PendingVerification",
  "subscriptionStatus": "Trialing"
}
```

The frontend should not repeatedly poll aggressively. If status polling is needed, use a reasonable interval such as 5 to 10 seconds and stop after a sensible timeout.

## Resend verification email

```text
POST {API_BASE_URL}/signups/{signupId}/resend-verification
```

Header:

```text
Authorization: Bearer SIGNUP-TOKEN
```

No JSON body is required.

Expected successful status: `202 Accepted`.

The API limits repeated resend attempts. Display a helpful message if the API returns `resend_limit_reached`.

## Email verification

The backend sends the owner an email containing a link to the Razor Pages verification page:

```text
https://inprem-003-site3.etempurl.com/Account/VerifyEmail
```

The complete email link also contains the signup ID and verification code. The frontend should not create or modify this link. The owner uses the page to confirm their email and choose a password.

## Paid checkout

Use checkout only when the selected registration requires it and the payment environment has been configured.

PayPal is not included in the current frontend testing scope.

For reference, the signup-token checkout request is:

```text
POST {API_BASE_URL}/checkout-sessions
```

Headers:

```text
Authorization: Bearer SIGNUP-TOKEN
Content-Type: application/json
Idempotency-Key: UNIQUE-REQUEST-ID
```

Body:

```json
{
  "planPriceId": "PLAN-PRICE-ID",
  "purpose": "new",
  "returnUrl": "https://frontend.example.com/checkout/complete",
  "cancelUrl": "https://frontend.example.com/checkout/cancelled"
}
```

The response contains a `checkoutSessionId` and an `approvalUrl`. Do not implement or test this part until the payment integration is ready.

## Error handling

Errors use `application/problem+json`.

Example validation response:

```json
{
  "title": "One or more fields are invalid.",
  "status": 400,
  "code": "validation_failed",
  "traceId": "TRACE-ID",
  "errors": {
	"owner.email": [
	  "The email address is invalid."
	]
  }
}
```

Common codes:

| Code | Meaning | Frontend action |
|---|---|---|
| `validation_failed` | One or more fields are invalid. | Display field errors from `errors`. |
| `captcha_failed` | CAPTCHA was missing or rejected. | Ask the user to complete CAPTCHA again and obtain a fresh token. |
| `plan_not_available` | The selected plan or price cannot be used. | Reload plans and ask the user to select again. |
| `slug_taken` | The organization slug is already used. | Show the suggested slug, if provided. |
| `email_in_use` | The owner email already has an account. | Tell the user to sign in or use another email. |
| `token_invalid` | The bearer token is not valid. | Restart the signup flow. |
| `token_expired` | The signup token has expired. | Restart the signup flow. |
| `resend_limit_reached` | Too many verification messages were requested. | Ask the user to wait. |
| `rate_limited` | Too many requests were made. | Wait and retry after the `Retry-After` value. |
| `internal_error` | The server encountered an unexpected problem. | Show a general error and provide the `traceId` to support. |

Do not display raw exception details to the user.

## Token and privacy rules

- Treat `signupToken` as a secret.
- Keep it only for the current signup flow.
- Do not put it in a URL.
- Do not send it to analytics tools.
- Do not write it to browser logs.
- Do not store it in a permanent account record.
- Use HTTPS for every request.
- Do not expose CAPTCHA secrets or API server secrets in frontend code.
- Do not allow the user to choose an arbitrary `planPriceId` without checking it against `GET /plans`.

## CORS requirement

The frontend website origin must be configured by the backend deployment. If the browser reports a CORS error, provide the exact frontend origin to the backend team. Do not solve this by adding `Access-Control-Allow-Origin: *` to production.

## Recommended frontend acceptance test

The frontend integration is ready for review when it can:

1. Load plans and display their active prices.
2. Load time zones.
3. Check slug availability.
4. Render the CAPTCHA widget.
5. Submit a valid signup with a fresh CAPTCHA token.
6. Show field-level validation errors.
7. Handle duplicate slug and duplicate email responses.
8. Show a clear email-verification message after successful signup.
9. Check signup status using the returned token.
10. Resend verification when requested.
11. Avoid exposing tokens and secrets in the browser console or URLs.
12. Work from the configured frontend origin without a CORS error.

## Related documents

- `docs/API-ENDPOINTS.md` - complete endpoint reference.
- `docs/API-TESTING.md` - API testing instructions.
- `InpremClockingApp/wwwroot/demo-site/README.md` - local demo-site and CAPTCHA test configuration.
