# 03 — Phase 1: Provider Console

**Entry:** Phase 0 exit checks pass. Decisions: C3-1, D14, D17.
**Branch:** `mt/phase1` from `mt/integration`.
**Design references:** sections 2.1, 5, 6, 13 (Phase 1).
**Site:** everything in this phase is served **only on the Admin site** (`admin.inpremclockin.org`, `SiteMode=Admin`).

Follow `00-guardrails.md`. Run all required suites after every step; update `PROGRESS.md`.

## UI rules (required)

- **No create or edit form on any list page.** List pages: title, search, filters, table, pagination, and a **+ New** button.
- Create opens its own page (organization creation is a 4-step wizard: Organization → Owner → Billing → Review).
- Detail pages are read-only with tabs; **Edit** opens a separate page with Save and Cancel; Post/Redirect/Get with a success message.
- Pop-ups only to confirm destructive actions, naming exactly what will happen.
- Reuse the app's Tailwind styles (`tw:` prefix) for a consistent look; do not modify existing `wwwroot` files (C1); add console-specific assets under `wwwroot/provider/`.

## Step 1.1 — Area, layout, authentication

- `Areas/Provider` with its own layout and navigation: Dashboard, Organizations, Plans, Billing, Webhook events, Notifications, Jobs, Audit log, Provider users, Settings. Routes at the root of the Admin site (`/`, `/tenants`, `/plans`, ...).
- Provider login page, two-factor enrolment and challenge, recovery-code sign-in (Level 1), sign-out, session limits (8 h absolute, 30 min idle).
- Authorization policies `ProviderOwner`, `ProviderBilling`, `ProviderSupport`; re-authentication (recent two-factor within 5 minutes) for sensitive actions.
- IP allow-list middleware for the Admin site from configuration (`AdminSite:AllowedNetworks`), returning 403 outside the list; disabled in local development.
- C3-1: remove SuperAdmin “Tenants” and “Invoices” links from `Pages/Shared/_Sidebar.cshtml` (owners' Billing link comes in Phase 2).

## Step 1.2 — Organizations

- List `/tenants`: search (name, slug, owner email), filters (status, plan, created date), columns (name, slug, status, plan, people count, last activity, created).
- Wizard `/tenants/new`: organization (name, slug with live availability check, IANA time zone, address, contact), owner (name, email, optional mobile), billing (plan, mode PayPal/Invoiced/Free, trial length), review. Creates tenant, owner user without password, Owner membership, subscription record, default settings in one transaction (`CreateTenantCommand`), sends invitation (24 h).
- Detail `/tenants/{id}` tabs: Overview, Subscription (read-only until Phase 2), Invoices and payments (placeholder until Phase 2/4), Admins (memberships, last login, two-factor status; resend invitation, send password reset, transfer ownership), Kiosk (open sessions; placeholder while `Kiosk:SessionMode` is off), Messages (placeholder until Phase 5), Audit, Notes.
- Edit `/tenants/{id}/edit`; suspend/reactivate with reason (confirmation dialog).

## Step 1.3 — Plans (local only)

- List, create, edit, retire plans and prices (`Plan`, `PlanPrice` per design 8/7.9). No PayPal calls yet; `PayPalPlanId` stays empty until Phase 2.

## Step 1.4 — Provider users and peer reset

- List provider users; invite (separate page); change role; disable. Owners cannot reset their own two-factor.
- Level 2 peer reset: another Provider Owner resets a person's two-factor after re-authenticating and entering a reason; notify all owners and the security mailbox; audit.

## Step 1.5 — Support sessions

- “Start support session” on a tenant (Owner or Support), reason required, default 60 minutes. Issues a time-limited support token that lets the provider user open the **App site** as Org Admin of that tenant via a one-time hand-off link; banner on every page; every request audited; billing actions blocked; owner can see support sessions in history. Because the cookie is host-only, the hand-off must be a signed, single-use, 60-second link validated on the App site.

## Step 1.6 — Audit log and dashboard shell

- Audit log list with filters (provider user, tenant, action, date) and CSV export.
- Dashboard with organization counts by status, trials ending (placeholder until billing), job failures from Hangfire, and the two-owner go-live warning.

## Step 1.7 — Remove replaced code (C4)

Remove `Pages/Platform/**`, `Controllers/Api/InvoicesController.cs` and `Services/TenantAdminService.cs`, and their registrations. Keep `BillingService`/`Models/Billing` until Phase 2 replaces them. All suites must still pass.

## Tests

- Authorization matrix for every console route × (anonymous, org admin, Provider Support, Billing, Owner).
- Page-pattern test: render every list page and assert it contains no `<form method="post">` other than search/filter (GET) forms.
- Every console write action produces exactly one audit entry (one test per action).
- Wizard creates all records atomically; failure in any step leaves nothing behind.
- IP allow-list blocks outside addresses.
- Support session: expiry, banner, billing blocked, audit per request, hand-off link single-use.
- Host isolation and tenant isolation suites extended to all new routes; baseline suite unchanged.

## Acceptance criteria

| ID | Criterion | Measure |
|---|---|---|
| P1-1 | Page pattern | 100% of list pages have no create/edit form (automated UI-pattern suite); developer walkthrough recorded in `PROGRESS.md` |
| P1-2 | Provider security | Console unreachable without two-factor; role-matrix tests pass for all three roles |
| P1-3 | Onboarding | Wizard + invitation completes in under 3 minutes on staging; owner can sign in on the App site |
| P1-4 | Audit | 100% of provider write actions audited (automated) |
| P1-5 | Support sessions | Expiry, banner, billing blocked, audited |
| P1-6 | Isolation | Isolation and authorization suites cover all console routes; 0 failures |
| P1-7 | Admin site protection | Requests from outside the allow-list refused (test) |

## Out of scope

PayPal calls, website API, Twilio. The old `/Platform` pages, `TenantAdminService` and `InvoicesController` (C4) are removed in a final step 1.7 of this phase, as a separate commit, after steps 1.1–1.6 pass.
