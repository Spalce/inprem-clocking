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

## Invoicing v2 and refunds (checked 2026-10-16, Phase 4 step 4.1)

- **What was checked.** PayPal's Invoicing v2 description (`developer.paypal.com/api/invoicing/v2/schema.json`, version 2.12.0) and the webhook event list. Invoicing v1 is under PayPal's `/deprecated/` pages. The SDK does not cover Invoicing, so it uses the typed client (`PayPalInvoiceProvider`, same `PayPal` HttpClient and token cache as step 2.2).
- **Endpoints used** (base `v2/invoicing`):

| Call | Endpoint | Retried by the client? |
|---|---|---|
| Create a draft | `POST /invoices` with `Prefer: return=representation` | No: no `PayPal-Request-Id`. Our invoice number makes a repeat safe: PayPal refuses a second invoice with the same number (`DUPLICATE_INVOICE_NUMBER`), and the client then finds the first with `POST /search-invoices`. |
| Send | `POST /invoices/{id}/send` (`send_to_recipient`, `send_to_invoicer`) | No (a repeat would email twice). `INVOICE_ALREADY_SENT` counts as sent; the payer page is then read from the invoice. |
| Read | `GET /invoices/{id}` | Yes (a read) |
| Cancel | `POST /invoices/{id}/cancel`; a draft is deleted with `DELETE /invoices/{id}` | Yes (cancelling twice changes nothing) |
| Record an offline payment | `POST /invoices/{id}/payments` (`method`, `payment_date`, `amount`) | No (a repeat would record it twice) |
| Record an offline refund | `POST /invoices/{id}/refunds` | No |

- **The payer's page** is the `href` of the send answer, or `detail.metadata.recipient_view_url` of the invoice.
- **Statuses.** PayPal has 16 (`DRAFT`, `SENT`, `SCHEDULED`, `PAID`, `MARKED_AS_PAID`, `CANCELLED`, `REFUNDED`, `PARTIALLY_PAID`, `PARTIALLY_REFUNDED`, `MARKED_AS_REFUNDED`, `UNPAID`, `PAYMENT_PENDING`, `AUTO_CANCELLED`, `PAID_EXTERNAL`, `REFUNDED_EXTERNAL`, `SHARED`). The client groups them (`PayPalInvoiceProvider.State`); an unknown one is `Unknown`, never guessed.
- **Payment methods.** PayPal knows `BANK_TRANSFER`, `CASH`, `CHECK`, `CREDIT_CARD`, `DEBIT_CARD`, `PAYPAL`, `WIRE_TRANSFER` and `OTHER`. It has no mobile money: we send `OTHER` and keep `MobileMoney` in our own records.
- **Giving money back** is not part of Invoicing (its "refunds" only record offline ones):
  - a subscription payment (a PayPal sale) is refunded with `POST v1/payments/sale/{id}/refund`;
  - a PayPal invoice payment (a capture) with `POST v2/payments/captures/{id}/refund`.
  - Both carry our `PayPal-Request-Id`, so PayPal refunds once and the client may retry.
  - **To confirm in the sandbox:** that the `payment_id` PayPal lists for a PayPal-paid invoice is the capture id the v2 refund takes.
- **Retries.** The resilience pipeline skips any request marked "no retry" (`PayPalRest.NoRetry`); everything else keeps the step 2.2 rules.
- **Errors** carry PayPal's error code (`details[0].issue`, else `name`) on `PaymentProviderException.Issue`, only when it looks like a code (upper-case letters, digits, `_`), so no values reach logs or pages.
- **Webhook events for step 4.3:** `INVOICING.INVOICE.CREATED`, `.UPDATED`, `.SCHEDULED`, `.PAID` (fully or partly paid, or a payment pending), `.CANCELLED`, `.REFUNDED`; `PAYMENT.CAPTURE.REFUNDED` and `.REVERSED` for invoice payments; `PAYMENT.SALE.REFUNDED` and `.REVERSED` (already handled) for subscription payments.

## Sources

- [PayPal Subscriptions API v1](https://developer.paypal.com/docs/api/subscriptions/v1/) and its [schema](https://developer.paypal.com/api/subscriptions/v1/schema.json), read 2026-10-15.
- [PayPal Invoicing API v2](https://developer.paypal.com/docs/api/invoicing/v2/) and its [schema](https://developer.paypal.com/api/invoicing/v2/schema.json) (version 2.12.0), read 2026-10-16.
- [PayPal webhook event names](https://developer.paypal.com/api/rest/webhooks/event-names/), read 2026-10-16.
- [PayPalServerSDK on NuGet](https://www.nuget.org/packages/PayPalServerSDK), versions 2.3.0 and 2.4.0, inspected 2026-10-15.
