# 06 — Phase 4: PayPal invoices, offline payments and refunds

**Entry:** Phase 2 exit checks pass (Phase 3 not required). PayPal sandbox with Invoicing available. Decisions D18, D28.
**Branch:** `mt/phase4` from `mt/integration`.
**Design references:** sections 8, 13 (Phase 4).

Follow `00-guardrails.md`. Run all required suites after every step; update `PROGRESS.md`. **Sandbox only.**

## Step 4.1 — Invoicing client

- Typed `HttpClient` for PayPal Invoicing v2 (create draft, send, get, cancel, record refund) behind `IPaymentProvider` (or a separate `IInvoiceProvider`). Confirm current endpoint paths and webhook event names in PayPal's Invoicing reference before coding; record them in `docs/multitenancy/paypal-notes.md`.
- Extend `Invoice` (platform): `Source` (Subscription|PayPalInvoice|Manual), `ExternalInvoiceId`, `PaymentUrl`, status, amounts, due date, sequential human-readable number (keep the existing numbering idea and the “one non-void invoice per period” rule).

## Step 4.2 — Console screens (Admin site)

- Billing → Invoices list (filters: tenant, status, date, source) with **+ New invoice** button opening a separate page (tenant, lines, due date, notes) → Review → Create and send through PayPal.
- Invoice detail: status, PayPal link, payments, actions (resend, cancel, record offline payment, refund) with confirmation dialogs and re-authentication for refunds.
- Record offline payment: separate page (date, method, reference, amount); role Owner/Billing.
- Refunds: Owner (any amount) and Billing (up to `Billing:RefundLimit`); PayPal refund call; result recorded via webhook.

## Step 4.3 — Webhooks and reconciliation

- Handle invoicing events (paid, cancelled, refunded) through the existing `BillingEvent` pipeline.
- Monthly reconciliation report: console totals vs PayPal transaction search for the month; differences listed; CSV export for accounting.

## Step 4.4 — Organization view

- In-app Billing page (App site) shows invoices with PayPal payment links and receipts. Website API `GET /account/invoices` returns the same data.

## Tests

- Invoice lifecycle with fake provider; sandbox end-to-end recorded.
- Role checks, re-authentication and audit for offline payments and refunds.
- Page-pattern test extended (no forms on list pages).
- Isolation: an organization sees only its own invoices.

## Acceptance criteria

| ID | Criterion | Measure |
|---|---|---|
| P4-1 | Invoices | Created, sent, paid and reflected via webhook in the sandbox |
| P4-2 | Offline payments and refunds | Role checks, re-authentication and audit (tests) |
| P4-3 | Reconciliation | Console totals equal PayPal sandbox totals for a test month |
