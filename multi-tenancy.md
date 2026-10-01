# Multi-Tenancy & SaaS Platform Implementation Plan

## Status (2026-10-01)

**Part 1 — core multi-tenancy — is done**, committed on `feature/multi-tenancy`, and verified
end-to-end: a real second tenant was created entirely through the UI (SuperAdmin onboarding ->
tenant admin login -> staff registration -> clock in/out -> report), confirming isolation and
per-tenant timezone resolution both hold with no visibility into another tenant's data at any
step. `TESTING.md` has a "Multi-tenant isolation" section covering this. Also fixed along the
way: `VolunteerService.GetByEmail` querying the wrong table, email search added to Reports, and
every API controller now carries its own `[Authorize]`/`AdminOnly` gate (see `ROLES.md`).

**Part 2 — SaaS platform extension — Phase 6 is done.** The app is moving from "multiple
organizations share one deployment" to "this is a product other organizations pay to use."
That requires a real provider portal, billing/invoicing, and renewal handling on top of the
tenant isolation Part 1 already built. Phase 6 added the `Subscription`/`Invoice` tables (purely
additive, same query-filter isolation as every other business table) and seeded every existing
tenant with a placeholder `$0`/month `Active` subscription so none are left undefined once
enforcement lands in Phase 9. No UI or billing logic exists yet — that starts with Phase 7.

## Goal

Part 1 made the app multi-tenant. Part 2 makes it operable as a SaaS product: a provider
(you) needs to see every tenant's subscription and payment status in one place, issue and track
invoices, and have the system handle renewals and non-payment without hand-maintained
spreadsheets. None of this changes how a tenant's own staff/volunteers/admins use the app day to
day — it's entirely new surface area at the provider level, plus one new read-only page for
tenant admins to see their own billing status.

## Decisions made for this pass (confirmed 2026-10-01)

- **Billing is manual/assisted, not self-serve.** No payment gateway integration now — no
  Stripe, no card capture, no online checkout. You set each tenant's price and generate
  invoices; the tenant pays you outside the app (bank transfer, check, etc.) and you mark the
  invoice paid. This mirrors the existing QuestPDF hour-report pattern rather than introducing a
  payments vendor.
- **The data model is shaped so a payment gateway can be added later without a redesign** (see
  decision #9 below) — this is a modeling precaution, not a feature being built now. No
  `IPaymentGateway` interface or Stripe stub is being written in Part 2; the seam is just that
  invoice/subscription rows carry enough optional fields that a future webhook handler could
  call the same `BillingService` methods a human clicks today.
- **Pricing is a flat recurring fee, set per tenant, not a global price or tiered catalog.**
  Each tenant's `Subscription` carries its own `Amount` directly — there's no `Plan` table yet.
  Per-seat or tiered pricing is an explicitly deferred extension (decision #9), not built now.
- **This pass produces the plan only.** No code changes in Part 2 until you review this
  document and say go, same review discipline as Part 1.

## Key architectural decisions (Part 2)

### 7. New `Subscription` and `Invoice` entities — not fields bolted onto `Tenant`

`Tenant` already has `IsActive`, which today is set on creation and displayed in the
`/Platform/Tenants` list but **is never actually checked anywhere** — a "suspended" tenant can
sign in and use the app exactly like an active one. That's fine for Part 1 (there was no billing
yet, so nothing to suspend for), but it means billing state needs its own home rather than more
ad-hoc fields on `Tenant`:

- **`Subscription`** (one per tenant, for now): `Id`, `TenantId` (FK), `Amount` (decimal, the
  tenant's flat recurring fee), `Currency` (default `"USD"`), `BillingCycle`
  (`Monthly`/`Annual`), `Status` (`Trialing`/`Active`/`PastDue`/`Suspended`/`Canceled`),
  `CurrentPeriodStart`, `CurrentPeriodEnd`, `CreatedAt`. Nullable `PaymentGateway` (default
  `null`/manual) and `ExternalSubscriptionId` columns exist from day one but are unused until a
  gateway is actually integrated — see decision #9.
- **`Invoice`**: `Id`, `TenantId` (FK), `SubscriptionId` (FK), `InvoiceNumber` (sequential,
  human-facing), `PeriodStart`, `PeriodEnd`, `Amount`, `Currency`, `IssuedDate`, `DueDate`,
  `Status` (`Draft`/`Issued`/`Paid`/`Overdue`/`Void`), `PaidDate` (nullable), `PaymentMethod`
  (nullable free text — "Bank transfer", "Check #1234"), `PaymentReference` (nullable), `Notes`
  (nullable).

Both get a `TenantId` column and join the existing set of tenant-scoped entities under the
Phase 2 global query filter — same mechanism, no new isolation primitive. A tenant's own Admin
can eventually see their own rows (Phase 9's read-only `/Billing` page); only a SuperAdmin sees
across tenants, the same `IgnoreQueryFilters()` pattern `TenantAdminService.GetAllTenantsAsync`
already uses.

### 8. `Tenant.IsActive` becomes a real, enforced gate

Right now `IsActive` is cosmetic. Part 2 makes it mean something: a single chokepoint (page
filter or middleware, exact mechanism decided in Phase 9) checks the signed-in user's tenant
before any tenant-scoped page renders, and if that tenant is inactive/suspended, shows an
"Account suspended — contact us" page instead — not a silent 403, not a broken dashboard.
SuperAdmin pages (`/Platform/*`) are explicitly exempt, same as today. This check is driven by
`Subscription.Status`, not set directly by a human toggling `IsActive` by hand (though the
provider portal can still force it for non-billing reasons — e.g. an organization that closed).

### 9. Explicit extension points (documented now, not built now)

Two things are very likely to be asked for later, and the schema above is shaped so adding them
doesn't require touching existing rows or tables:

- **Payment gateway (e.g. Stripe)**: `Subscription.PaymentGateway`/`ExternalSubscriptionId` and
  a future `Invoice.ExternalPaymentId` are the only new columns a real integration would need.
  The actual charge/webhook handling would live behind the same `BillingService.MarkInvoicePaid`
  method the provider portal calls manually today — a Stripe webhook controller becomes just
  another caller of that method, not a parallel code path.
- **Per-seat or tiered pricing**: introduce a `Plan` entity (`Name`, `DefaultAmount`, optional
  `SeatCap`) and a nullable `Subscription.PlanId`, with `Subscription.Amount` staying as a
  per-tenant override. Flat pricing today is just "every tenant has a `Subscription` row with no
  `PlanId`" — no migration of existing rows needed when plans are introduced.

### 10. Renewals are generated, not charged

With no payment gateway, "renewal" means: as a subscription's `CurrentPeriodEnd` approaches, the
system creates the next `Invoice` automatically (status `Issued`) and advances the period dates
— it does not move money. A small daily background check (`BillingBackgroundService`, an
`IHostedService` — the app has no background-job infra today, this would be the first) does two
things:
1. Generate the next period's invoice N days before `CurrentPeriodEnd` (configurable, default
   14).
2. Flag `Subscription.Status = PastDue` for any invoice still unpaid past its `DueDate`, and
   `Suspended` (which trips decision #8's gate) after a grace period past that (configurable,
   default 14 days past due).

All of this is visible and overridable from the provider portal — a SuperAdmin can always
manually issue, void, or mark an invoice paid ahead of the background job.

### 11. Provider Portal grows from one page into a small area

Today `/Platform/Tenants` only creates and lists tenants. Part 2 expands `/Platform/*` into:

- **`/Platform/Tenants`** (enhanced) — list gains a billing-status badge per tenant
  (Active/Trialing/Past Due/Suspended) alongside the existing Yes/No `IsActive` column; the
  create-tenant form gains initial subscription fields (amount, cycle, optional trial length).
- **`/Platform/Tenants/{id}`** (new) — tenant detail/edit: edit name/timezone/address/contact
  (closes a `ROLES.md` "Known gap" — today nothing can be edited after creation), list the
  tenant's admins (closes another known gap), force-activate/suspend, view and edit that
  tenant's `Subscription`, and see its invoice history.
- **`/Platform/Invoices`** (new) — cross-tenant invoice queue: everything `Issued`/`Overdue`
  across all tenants in one place, manual "generate invoice now" action, "mark paid" action
  (captures date/method/reference), PDF download per invoice (same `QuestPDF` pattern as hour
  reports).

### 12. Tenant-facing `/Billing` page (new, read-only)

A new `AdminOnly` page, scoped to the signed-in Admin's own tenant via the existing query filter
— current plan amount/cycle, current period dates, and a read-only invoice history with PDF
downloads. No "pay now" button (manual billing only, decision above) — just visibility, with a
"contact us" message for payment, consistent with the fact that no gateway exists yet.

## What's explicitly out of scope for this pass

Called out so it's clear what's being deferred rather than overlooked:

- Online payments, card capture, or any payment gateway integration (Stripe or otherwise).
- Self-serve tenant signup (still SuperAdmin-created only, as decided in Part 1).
- Per-seat or tiered pricing (extension point only, see decision #9).
- Automatic dunning emails, payment retries, or any outbound email at all — the background job
  changes status fields; it doesn't send anything. Notification is a separate, later feature.
- Multi-currency handling beyond storing a `Currency` string (no FX, no per-currency formatting
  rules) and tax/VAT calculation on invoices.
- A platform-level KPI dashboard (MRR, churn, etc.) — natural follow-on once billing data exists,
  but not in this phased plan; easy to add once `Subscription`/`Invoice` exist.

## Guardrails (carried over from Part 1, apply to Part 2 too)

- All work happens on `feature/multi-tenancy` (or a follow-on branch off it) until reviewed;
  nothing merges to `main` or gets pushed without your explicit go-ahead.
- New tables are purely additive — `Subscription`/`Invoice` don't touch or reshape any existing
  row, so Part 2 carries none of Part 1's "retrofit onto live data" risk. A backup before Phase 6
  is still good practice but this phase has no backfill step.
- Every phase ends with a real migration + live build-and-click-through verification before
  moving to the next, same discipline as Part 1.
- `Tenant.IsActive` actually gating access (decision #8) is the one change in Part 2 with real
  blast radius for existing tenants — it gets its own explicit verification step (Phase 9) with
  Inprem's own tenant confirmed still `Active`/unaffected before anything else touches it.

## Phased plan (Part 2)

**Phase 6 — Billing data model — done (2026-10-01)**
Added `Subscription` and `Invoice` entities (`Models/Billing/`) + two migrations
(`MultiTenancy_Phase6a_AddBillingTables` for schema, `MultiTenancy_Phase6b_SeedSubscriptions` for
data), wired into the existing tenant query-filter mechanism and enum-to-string conversions
(so `Status`/`BillingCycle` read as text, not bare ints, when inspected directly in SSMS). Every
existing tenant (Inprem plus the Part 1 test tenant) was seeded with a placeholder `$0`/month
`Active` subscription — a real amount (or a decision to exempt Inprem as the house account) is
still an open item for you, tracked under "What you'll need to handle" below. No UI yet.
Verified: full solution build succeeds; migration applies cleanly against the local DB with no
EF warnings; a live smoke test confirmed the app still serves pages normally with the new
tables in place; both tables carry working `TenantId` foreign keys and the same query-filter
construct already proven in Part 1.

**Phase 7 — Provider portal expansion**
`/Platform/Tenants/{id}` detail/edit page (name/timezone/address/contact edit, admins-per-tenant
list, force-activate/suspend, view/edit that tenant's `Subscription`). Enhance the
`/Platform/Tenants` list with a billing-status badge.
Verify: SuperAdmin can edit an existing tenant's details and subscription amount; an Admin
cannot reach any `/Platform/*` route (unchanged from today).

**Phase 8 — Invoicing & renewals**
`BillingService` (generate invoice, mark paid, void, renew-period), `/Platform/Invoices` queue
page, PDF invoice generation (QuestPDF, same pattern as hour reports), and the
`BillingBackgroundService` daily job (generate upcoming invoices, flag past-due/suspended per
decision #10).
Verify: manually generate and mark an invoice paid end-to-end through the UI; confirm the
background job correctly advances a test subscription's period and flags a second,
intentionally-unpaid test subscription as `PastDue` then `Suspended` on schedule.

**Phase 9 — Enforcement & tenant-facing billing page**
Wire `Subscription.Status`/`Tenant.IsActive` into a real access gate (decision #8) with the
"Account suspended" page; add the tenant-facing read-only `/Billing` page.
Verify: a suspended test tenant's Admin is blocked from every tenant-scoped page and shown the
suspended message, not an error; Inprem's own tenant (and the other already-existing test
tenant) are confirmed unaffected before and after this phase ships.

**Phase 10 — Platform dashboard (optional)**
Provider-level KPIs on a `/Platform` landing page — tenant counts by status, sum of active
subscription amounts, overdue invoice count/total — mirrors `BackOffice.cshtml`'s existing
aggregate-in-memory pattern. Not required for the billing/invoicing/renewal flow to work; purely
a convenience view once the data exists. Can be deferred past the rest of Part 2 without
blocking anything.

## What I'll handle

- All schema, service, controller, and page changes for every Part 2 phase, isolated the same
  way Part 1 was.
- Writing and running migrations (all additive in Part 2 — no backfill risk like Part 1 had).
- Functional and isolation verification after each phase, in-browser.
- Keeping this document updated with progress/status as phases complete.

## What you'll need to handle

- Review checkpoints — at minimum after Phase 8 (before the background job can ever suspend a
  real tenant) and Phase 9 (the enforcement gate going live). Tell me to continue, adjust, or
  stop.
- Deciding actual default values when we get there: grace-period length, how many days before
  renewal an invoice gets generated, Inprem's own subscription amount/cycle (or whether Inprem
  is simply exempt from billing as the house account).
- The final merge to `main` — I will not do this myself, per your standing instruction.
