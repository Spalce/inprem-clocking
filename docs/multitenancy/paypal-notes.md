# PayPal notes

Findings from checking PayPal's current documentation and SDK (Phase 2 step 2.2), and the choices made.

## Return and cancel addresses (checked 2026-10-15)

- **What PayPal says.** In PayPal's Subscriptions API description (`developer.paypal.com/api/subscriptions/v1/schema.json`, version 1.11), the create-subscription body's `application_context` is marked `"deprecated": true` ("DEPRECATED. The application context, which customizes the payer experience…"). It is still the only per-subscription place for `return_url` and `cancel_url`, and PayPal names no replacement: the request has no `payment_source.paypal.experience_context` for subscriptions. The plan revise body (`/revise`) still has `application_context` without the deprecation mark.
- **Where else the addresses can go.** A billing plan has `merchant_preferences` with `return_url` and `cancel_url`, fixed per plan.
- **The SDK agrees.** PayPalServerSDK 2.4.0 documents `CreateSubscriptionRequest.ApplicationContext` as "DEPRECATED" and still sends it.
- **Decision (developer, 2026-10-15): both.**
  - Every subscription is created with `application_context.return_url` / `cancel_url` (the in-app Billing page or the website, from the allow-list).
  - Every PayPal plan also gets `merchant_preferences` pointing to our app's fixed pages (`PayPal:PlanReturnUrl`, `PayPal:PlanCancelUrl`).
  - If PayPal ever stops honouring `application_context`, buyers still come back to our app. PayPal adds `subscription_id` to the return address, so the page can find the checkout session and forward the buyer.
  - Confirmation never depends on the return address anyway (design 7.5).

## SDK version

- **PayPalServerSDK 2.4.0** (2026-08-21). Version 2.3.0 has no `status` on its `Subscription` model, which confirmation depends on; 2.4.0 has `Status`, `StatusChangeNote` and `StatusUpdateTime`.
- The SDK covers billing plans and subscriptions (create, get, revise, cancel, and more). It does **not** cover catalog products or webhook verification: those use a typed `HttpClient` (`POST /v1/catalogs/products`, `POST /v1/notifications/verify-webhook-signature`).
- The SDK's status enum prints as `ApprovalPending` rather than `APPROVAL_PENDING`; `PayPalStatus.Parse` accepts both.

## How the client is set up

- **One named `HttpClient`** (`PayPal`) for the SDK and the typed calls:
  - a timeout per attempt (`PayPal:TimeoutSeconds`, default 30) and for the whole call (3 times that);
  - 3 retries with jitter for network errors, 5xx, 408 and 429;
  - the SDK's own retries are off.
- **Creates are safe to retry:** product, plan and subscription creates send a `PayPal-Request-Id`, so PayPal makes them once.
- **Logging** (`PayPalLoggingHandler`): method, path, status, time and PayPal's `PayPal-Debug-Id` (the correlation id for PayPal support). Never headers or bodies.
  - The framework's HttpClient logging is removed for this client: at Trace level it wrote the request headers, the app's Basic credentials included (found by a test in step 2.2).
- **Tokens:** the typed calls cache an OAuth token until 5 minutes before it expires; the SDK keeps its own.
- **Configuration** (section `PayPal`, checked at startup):
  - `Mode` (`Sandbox` | `Live`); Live is refused unless `AllowLive=true` (not in Phase 2).
  - `ClientId` and `Secret`, from user-secrets or the host, never the repository.
  - `WebhookId`; `ReturnUrlAllowList` (https origins); `PlanReturnUrl` and `PlanCancelUrl` (on an allowed origin); `TimeoutSeconds` (5 to 120).
  - Without a `ClientId`, PayPal is "not configured": the app runs, PayPal actions say so, and every webhook is refused.

## Webhook verification

- `POST /v1/notifications/verify-webhook-signature` with:
  - the five transmission headers (`PAYPAL-AUTH-ALGO`, `-CERT-URL`, `-TRANSMISSION-ID`, `-TRANSMISSION-SIG`, `-TRANSMISSION-TIME`);
  - our `webhook_id`;
  - the event exactly as received (`webhook_event`).
- Only `verification_status = SUCCESS` passes. A missing header, a body that is not JSON, a missing webhook id or a PayPal error all refuse the event; PayPal redelivers later.

## Sources

- [PayPal Subscriptions API v1](https://developer.paypal.com/docs/api/subscriptions/v1/) and its [schema](https://developer.paypal.com/api/subscriptions/v1/schema.json), read 2026-10-15.
- [PayPalServerSDK on NuGet](https://www.nuget.org/packages/PayPalServerSDK), versions 2.3.0 and 2.4.0, inspected 2026-10-15.
