# Inprem Clock-in Website API

## Purpose

This document lists the public website API endpoints used for tenant registration, signup verification, checkout, and existing-customer billing access.

## What to give out

### Give the supervisor

Give the supervisor the deployed API base URL and the health-check URL:

```text
API base URL:
https://inprem-003-site3.etempurl.com/api/public/v1

Health check:
https://inprem-003-site3.etempurl.com/api/public/v1/health
```

Also state that the tenant-registration endpoint is:

```text
POST https://inprem-003-site3.etempurl.com/api/public/v1/signups
```

The base URL is a deployment value. It is valid only while that deployment is running the current API version. Do not give out database passwords, SMTP passwords, CAPTCHA secrets, or bearer tokens.

### Give the frontend developers

Give them:

- The API base URL for their environment.
- The endpoint list below.
- The request and response formats.
- The CAPTCHA site-key configuration for their frontend.
- The CORS origin that has been configured for their website.
- The rule that `POST` requests must be sent from frontend code, Postman, or another HTTP client; they cannot be tested by opening the URL in a browser.

The frontend should use environment-specific base URLs instead of hard-coding the production URL.

## Base URL

```text
https://inprem-003-site3.etempurl.com/api/public/v1
```

All paths below are relative to this base URL.

## Public endpoints

These endpoints do not require a bearer token.

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/health` | Confirms that the API is online. |
| `GET` | `/plans` | Returns public plans and active prices. The frontend obtains `planPriceId` values from this response. |
| `GET` | `/timezones` | Returns supported time zones for the registration form. |
| `GET` | `/slugs/{slug}/availability` | Checks whether an organization slug is available. |
| `POST` | `/signups` | Creates the tenant, owner account, membership, profile, subscription, and signup record. |

### Health

```text
GET https://inprem-003-site3.etempurl.com/api/public/v1/health
```

### Plans

```text
GET https://inprem-003-site3.etempurl.com/api/public/v1/plans
```

Use the returned active `planPriceId` in the signup request. Do not invent a plan price ID.

### Time zones

```text
GET https://inprem-003-site3.etempurl.com/api/public/v1/timezones
```

### Slug availability

```text
GET https://inprem-003-site3.etempurl.com/api/public/v1/slugs/example-center/availability
```

### Create tenant signup

```text
POST https://inprem-003-site3.etempurl.com/api/public/v1/signups
```

Required headers:

```text
Content-Type: application/json
Idempotency-Key: a-unique-request-id
```

Example request body:

```json
{
  "organizationName": "Example Center",
  "slug": "example-center",
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
  "planPriceId": "PLAN-PRICE-ID-FROM-GET-PLANS",
  "mode": "trial",
  "captchaToken": "TOKEN-FROM-CAPTCHA-WIDGET"
}
```

A successful response is `201 Created` and contains a temporary signup token:

```json
{
  "signupId": "SIGNUP-ID",
  "signupToken": "TEMPORARY-SIGNUP-TOKEN",
  "signupTokenExpiresAt": "2026-01-01T12:00:00Z",
  "status": "PendingVerification",
  "nextAction": "verify_email"
}
```

The signup token is secret, expires after two hours, and is scoped to the signup that created it.

## Signup-token endpoints

These endpoints require:

```text
Authorization: Bearer SIGNUP-TOKEN
```

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/signups/{signupId}` | Returns the status of the caller's signup. |
| `POST` | `/signups/{signupId}/resend-verification` | Resends the owner's email-verification message. |
| `POST` | `/checkout-sessions` | Starts checkout for a new paid registration. |
| `GET` | `/checkout-sessions/{checkoutSessionId}` | Returns checkout status. |
| `POST` | `/checkout-sessions/{checkoutSessionId}/confirm` | Confirms the checkout session. |
| `POST` | `/checkout-sessions/{checkoutSessionId}/cancel` | Cancels the checkout session. |

Protected endpoint URLs:

```text
GET  https://inprem-003-site3.etempurl.com/api/public/v1/signups/{signupId}
POST https://inprem-003-site3.etempurl.com/api/public/v1/signups/{signupId}/resend-verification
POST https://inprem-003-site3.etempurl.com/api/public/v1/checkout-sessions
GET  https://inprem-003-site3.etempurl.com/api/public/v1/checkout-sessions/{checkoutSessionId}
POST https://inprem-003-site3.etempurl.com/api/public/v1/checkout-sessions/{checkoutSessionId}/confirm
POST https://inprem-003-site3.etempurl.com/api/public/v1/checkout-sessions/{checkoutSessionId}/cancel
```

### Create checkout session

Required headers:

```text
Authorization: Bearer SIGNUP-TOKEN
Content-Type: application/json
Idempotency-Key: a-unique-request-id
```

Request body:

```json
{
  "planPriceId": "PLAN-PRICE-ID",
  "purpose": "new",
  "returnUrl": "https://frontend.example.com/checkout/complete",
  "cancelUrl": "https://frontend.example.com/checkout/cancelled"
}
```

For a signup token, `purpose` must be `new`.

## Existing-customer billing endpoints

These are not needed for new tenant registration. They use a billing access token obtained through the billing-access flow.

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/billing-access/requests` | Requests an email containing a billing access link. |
| `POST` | `/billing-access/sessions` | Exchanges the emailed one-time code for a billing access token. |
| `GET` | `/account/subscription` | Returns subscription details. |
| `GET` | `/account/invoices` | Returns the organization's invoices. |
| `POST` | `/account/subscription/cancel` | Cancels the PayPal subscription. |
| `POST` | `/account/subscription/keep` | Returns the cancellation policy response; cancellation cannot be withdrawn. |

Protected billing account requests use:

```text
Authorization: Bearer BILLING-ACCESS-TOKEN
```

## Verification page

The verification email links the owner to the Razor Pages application at:

```text
https://inprem-003-site3.etempurl.com/Account/VerifyEmail
```

The complete email link also includes the signup ID and verification code. The frontend should not construct or expose verification codes itself.

## Errors

Errors use `application/problem+json` and contain a stable `code` and `traceId`. Common codes include:

- `validation_failed`
- `captcha_failed`
- `plan_not_available`
- `slug_taken`
- `email_in_use`
- `token_invalid`
- `token_expired`
- `resend_limit_reached`
- `rate_limited`
- `internal_error`

The frontend should use the `errors` object for field messages and retain the `traceId` for support requests.

## OpenAPI documentation

If enabled on staging, Swagger is available at:

```text
https://inprem-003-site3.etempurl.com/api/public/docs
```

The OpenAPI JSON is:

```text
https://inprem-003-site3.etempurl.com/api/public/docs/public-v1/openapi.json
```

Swagger is optional and is normally enabled only on staging. It is not the API itself; the API is the `/api/public/v1` endpoint set.
