# 04 — Phase 2: PayPal core and in-app Billing page

**Entry:** Phase 1 exit checks pass. Decisions: C3-1 to C3-4, D18–D26. PayPal **sandbox** app credentials (and, once the app is reachable over HTTPS, the sandbox webhook id) supplied by the developer through user-secrets.
**Branch:** `mt/phase2` from `mt/integration`.
**Design references:** sections 7.4, 7.5, 8, 10.3, 13 (Phase 2).

Follow `00-guardrails.md`. Run all required suites after every step; update `PROGRESS.md`. **Sandbox only.**

## Step 2.1 — Domain and data (migration M1 extension)

- Entities: `Subscription` (platform version: TenantId, PlanPriceId, BillingMode PayPal/Invoiced/Free, Status Trialing/PendingActivation/Active/PastDue/Suspended/Expired/Canceled/Closed, TrialEndsUtc, CurrentPeriodStartUtc, CurrentPeriodEndUtc, CancelAtPeriodEnd, OutstandingBalance, ExternalSubscriptionId unique, overrides), `CheckoutSession` (Id, Reference, TenantId or SignupId, Purpose new/convert/resubscribe/change_plan, PlanPriceId, Status Created/AwaitingApproval/Approved/Active/Cancelled/Expired/Failed, ExternalSubscriptionId, ApprovalUrl, ReturnUrl, CancelUrl, ExpiresUtc, IdempotencyKey), `Payment`, `BillingEvent` (ExternalEventId PK, EventType, ResourceId, RawJson, ReceivedUtc, ProcessedUtc, Attempts, Error).
- Additive migration; idempotent + down scripts; verification queries.
- Existing Inprem organization: subscription per D26 (Free house plan unless changed).

## Step 2.2 — PayPal client (Infrastructure)

- `IPaymentProvider` implementation using `PayPalServerSDK` for subscriptions and a typed `HttpClient` for webhook verification (`POST /v1/notifications/verify-webhook-signature`) and later invoicing. Cached OAuth token; Polly retries for transient errors; timeouts; correlation ids in logs; never log full payloads containing personal data.
- Options `PayPalOptions { Mode: Sandbox|Live, ClientId, ClientSecret, WebhookId, ReturnUrlAllowList[] }` validated at startup; Live refused unless an explicit `PayPal:AllowLive=true` is set (not in this phase).
- **Verify first:** PayPal's create-subscription reference documents `application_context.return_url` / `cancel_url` but marks `application_context` deprecated. Check the current PayPal documentation; implement whatever PayPal currently recommends; record the finding in `docs/multitenancy/paypal-notes.md`. If unclear, stop and ask.

## Step 2.3 — Plan sync

- Console action “Publish to PayPal” creates a PayPal product (once) and a billing plan per plan price, with `payment_preferences.auto_bill_outstanding = true` and `payment_failure_threshold` from D23; stores `PayPalPlanId`. Price changes create a new plan; existing subscribers are moved deliberately.

## Step 2.4 — Checkout (shared by in-app page and, in Phase 3, the website)

Use cases:
- `StartCheckout(purpose, planPriceId, returnUrl, cancelUrl, idempotencyKey)`: validates URLs against the allow-list; looks up the PayPal plan id from **our** catalogue; creates `CheckoutSession`; creates the PayPal subscription with `plan_id`, `custom_id = CheckoutSession.Reference`; stores the subscription id; returns the approve link.
- `ConfirmCheckout(sessionId)`: fetches the subscription from PayPal; verifies `custom_id`, `plan_id`, amount/currency against the catalogue, not linked to another tenant, session not expired; maps PayPal status (`APPROVAL_PENDING`/`APPROVED`/`ACTIVE`/others) to session status; activates via `ActivateSubscription` when ACTIVE.
- `ActivateSubscription`: idempotent (unique constraint + status check), sets tenant subscription Active and period dates, writes audit, queues confirmation email. Called by confirm, webhook and reconciliation; activation happens exactly once.
- Change plan uses PayPal subscription revision; cancel uses the PayPal cancel endpoint (at period end semantics recorded locally).

## Step 2.5 — Webhooks (App site, `/webhooks/paypal`)

- Verify signature with PayPal's API using the transmission headers and our webhook id; reject unsigned/invalid (400, logged).
- Insert `BillingEvent` by `ExternalEventId` (duplicate → 200, no-op); return 200 fast; enqueue Hangfire job `ProcessBillingEvent(id)`.
- Job re-fetches the resource from PayPal and applies: ACTIVATED → activate; `PAYMENT.SALE.COMPLETED` → record payment, paid invoice record, advance period, clear PastDue; `BILLING.SUBSCRIPTION.PAYMENT.FAILED` → PastDue; `SUSPENDED` → Suspended; `CANCELLED` → Canceled (access to period end); `EXPIRED` → Expired; `UPDATED` → refresh; `PAYMENT.SALE.REFUNDED`/`REVERSED` → record.
- Console “Webhook events” page lists events with status and a Replay button (Owner/Billing).

## Step 2.6 — Reconciliation jobs

- Every 5 minutes: open checkout sessions in AwaitingApproval/Approved → `ConfirmCheckout`; expire sessions past `ExpiresUtc`.
- Daily: compare every PayPal-mode subscription with PayPal; correct drift; audit each correction; alert provider (email for now; SMS in Phase 5).
- Trial expiry is evaluated lazily from `TrialEndsUtc` on each request and also by a daily job.

## Step 2.7 — Entitlements and enforcement (C3-3, C3-4)

- `IEntitlementService`: can write, can use kiosk, can add people (plan limit), can send SMS.
- A global page/endpoint filter on the App site blocks state-changing requests (POST/PUT/DELETE) for Suspended/Expired/Closed tenants with a clear message; GET, reports and exports remain available. Do not edit approved pages; the filter is registered in `Program.cs`.
- Kiosk: the same filter blocks kiosk actions with a friendly message.
- C3-2: one partial include in `_MainLayout.cshtml` rendering a status banner (trial days left, payment problem, read-only notice); renders nothing for Active tenants, so active organizations see no change.

## Step 2.8 — In-app Billing page (App site, Owner only)

- `Areas/Billing`: current plan and status, change plan, subscribe (redirect to PayPal approve link), return page that calls confirm and polls status (2 s for 30 s, then 10 s up to 2 min), cancel at period end, invoice/payment history.
- C3-1: add “Billing” link to the sidebar for organization Owners only.
- Console Organization detail → Subscription tab and Billing pages show real data; actions: extend trial, grant free access, sync with PayPal, cancel.

## Step 2.9 — Remove replaced billing code (C4)

Migrate any existing rows from the old `Subscriptions`/`Invoices` tables into the platform tables (migration M5, with verification counts), switch every reader to the new services, then remove `Services/BillingService.cs`, `Services/BillingBackgroundService.cs` and `Models/Billing/*` registrations and code. The old tables are dropped only in Phase 7 (M6).

## Tests

- Unit tests for state mapping and every transition in the design's subscription and checkout state diagrams.
- Integration tests with a fake `IPaymentProvider` for: redirect-only never activates; activation exactly once when confirm, webhook and reconciliation all fire (in any order); mismatched `custom_id`/plan/amount → Failed; duplicate and out-of-order webhooks; invalid signature rejected.
- Sandbox end-to-end (manual, recorded in `PROGRESS.md`; webhooks need the app on a public HTTPS address such as a test site or a tunnel like ngrok): subscribe, renew, fail (sandbox card failure), suspend, recover, cancel, change plan.
- Enforcement: Suspended/Expired cannot save or use kiosk; reports/exports still work; Active tenants pass the full baseline suite.

## Acceptance criteria

| ID | Criterion | Measure |
|---|---|---|
| P2-1 | Checkout | Sandbox subscription created, approved and Active from the in-app Billing page |
| P2-2 | Confirmation | Never Active from a redirect alone (test); activation exactly once across confirm, webhook, reconciliation (test) |
| P2-3 | Webhooks | Unsigned/tampered rejected; duplicates ignored; out-of-order handled; 100% visible in console |
| P2-4 | Lifecycle | Every state-diagram transition exercised in the sandbox |
| P2-5 | Enforcement | Suspended/Expired cannot save or use kiosk; reports and exports work |
| P2-6 | Reconciliation | A deliberately dropped webhook corrected within one cycle |
| P2-7 | Behaviour | Baseline suite 100% for Active organizations |

## Out of scope

Website API (Phase 3), PayPal Invoicing and refunds UI (Phase 4), SMS (Phase 5), live PayPal.
