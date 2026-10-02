# Multi-Tenancy & SaaS Platform Test Guide

A guided walkthrough for testing everything built on `feature/multi-tenancy`: tenant
isolation (Part 1), and the provider portal, billing/invoicing, and renewal handling
(Part 2) described in `multi-tenancy.md`. Pair this with `TESTING.md` for everything
else in the app (clocking, reports, kiosk pages) - that guide's own multi-tenancy
section now just points here.

## 1. Setup

```bash
# from the repo root
dotnet build InpremClockingApp.sln
dotnet run --project InpremClockingApp
```

App runs at `https://localhost:7078` (and `http://localhost:5278`/`5250` depending on
launch profile).

### Test accounts

The database was cleaned up and reseeded for this guide (2026-10-01) - any
`admin@test.com` / `firstadmin@testtenant.org` / "Test Tenant Org" accounts you may
have seen in earlier notes no longer exist. Current accounts:

| Account | Password | Role | Tenant |
|---|---|---|---|
| `admin@inprem.org` | *(your existing password - unchanged)* | Admin | Inprem Holistic Community Resource Center (Id 1) |
| `admin@super.org` | `SuperTest@2026!` | SuperAdmin | none (platform operator) |
| `admin@acmetest.example` | `AcmeTest@2026!` | Admin | Acme Test Org (Id 9) |

`admin@inprem.org` was left completely untouched - don't reset or delete it. The
other two are disposable test accounts created for this guide; recreate them anytime
using section 9 below if you clean them up.

**Acme Test Org** was created fresh through the real `/Platform/Tenants` onboarding
form (not inserted directly via SQL), timezone `America/Chicago` (one hour behind
Inprem's `America/New_York` - useful for visually confirming per-tenant time), with
an Active `$29.99`/month subscription already set so the billing sections below have
something real to work with immediately. It starts with zero staff/volunteers - you
create one as part of section 4.

---

## 2. Login behavior

These cover bugs found and fixed this session - worth re-checking after any future
change to `Login.cshtml`/`Login.cshtml.cs`.

- [ ] Visit `/Identity/Account/Login` directly (no query string). The page should
      render the **kiosk-styled** look (green "Sign In" button, "Sign in to clock in
      or out" subtitle) - this is the default for "no specific destination requested".
- [ ] Log in as `admin@super.org` from that same direct visit. You should land on
      `/Platform/Tenants`, **not** a kiosk page - a SuperAdmin has no tenant, so the
      generic kiosk fallback is never a meaningful destination for one.
- [ ] Log out. Log in as `admin@acmetest.example` the same way (direct visit, no
      query string). You should land on `/VolunteerAttendance` - the kiosk fallback
      is correct for an ordinary Admin.
- [ ] While signed out, visit the bare root `https://localhost:7078`. You should see
      the same kiosk-styled login page (not the Back Office look) - visiting root
      while unauthenticated produces `ReturnUrl=/`, which is treated the same as "no
      destination requested".
- [ ] While signed out, try to visit `/Platform/Tenants` directly. You should be
      challenged to log in with that exact page as the return destination - after
      logging in as `admin@super.org`, you should land back on `/Platform/Tenants`
      (an explicit destination always wins over the kiosk-vs-SuperAdmin fallback
      logic above).

## 3. Provider portal (`/Platform/*`, SuperAdmin only)

Log in as `admin@super.org` for this whole section.

**Access control**
- [ ] Log out, log in as `admin@acmetest.example` (an ordinary Admin), and try to
      visit `/Platform/Tenants` and `/Platform/Invoices` directly. Both should
      redirect to an **Access Denied** page, not render.
- [ ] The sidebar's **Tenants** / **Invoices** links should not even appear for this
      account.

**Tenants list (`/Platform/Tenants`)**
- [ ] Log back in as `admin@super.org`. The list shows both tenants, each with a
      colored billing-status badge (both should read **Active**, green).
- [ ] The create-tenant form at the top still works: create a quick throwaway tenant
      (any name/timezone/admin), confirm it appears in the list immediately with an
      **Active** badge and a working placeholder subscription - then delete it again
      via SSMS/`sqlcmd` if you don't want to keep it around (there's no delete button
      in the UI yet - see `ROLES.md`'s "Known gaps").

**Tenant detail (`/Platform/Tenants/9`, Acme Test Org)**
- [ ] Click **Manage** on Acme Test Org's row. You should see its name/timezone/
      address/contact (address/contact blank - never filled in), a **Suspend Tenant**
      button, its `$29.99`/Monthly/Active subscription, an admins list showing
      `admin@acmetest.example`, and an (empty, for now) invoices section.
- [ ] Edit the Address/Contact Info fields and Save - they should persist and show on
      reload.
- [ ] Click **Suspend Tenant**. The status line should flip to "Suspended" and the
      button should now read **Activate Tenant**. (This doesn't block anything yet -
      see Known gaps / multi-tenancy.md Phase 9 - it's a pure data toggle for now.)
      Click **Activate Tenant** again to restore it before continuing.
- [ ] Edit the subscription's Amount to something else (e.g. `39.99`) and Save -
      confirm it persists, then set it back to `29.99` for the rest of this guide.

## 4. Tenant isolation

- [ ] Log in as `admin@acmetest.example`. Dashboard/Manage Staff/Manage Volunteers
      should all show **0** - none of Inprem's 25/25 records.
- [ ] Register a new staff member and clock them in/out through the kiosk flow,
      entirely within this session. The displayed kiosk time should read **one hour
      behind** whatever Inprem's own kiosk currently shows (America/Chicago vs.
      America/New_York) at the same real-world instant.
- [ ] Download that staff member's PDF hour report - the header should show **Acme
      Test Org**, never "Inprem Holistic Community Resource Center".
- [ ] While signed in as this account, try to reach one of Inprem's real
      Staff/Volunteer IDs directly by URL (e.g. `/HoursWorked?Id=1&Type=staff`) -
      this must fail (not found / no data), never show Inprem's actual record.
- [ ] Register a staff/volunteer using an email address already used by one of
      Inprem's own staff/volunteers - this should succeed (two different
      organizations may share an email; only a duplicate *within* the same tenant is
      blocked).
- [ ] Switch back to `admin@inprem.org` and confirm nothing changed there - same
      dashboard counts, same clocking behavior, same PDF branding as always.

## 5. Cross-tenant admin accounts

Two different tenants can now have an admin sharing the same email/username
(code review fix, 2026-10-01) - this section confirms sign-in still resolves to the
*correct* account.

- [ ] As `admin@super.org`, create one more throwaway tenant whose admin email is
      exactly `admin@acmetest.example` (the same address Acme Test Org already uses)
      but a **different** password, e.g. `OtherOrgPassword@2026!`. This should
      succeed - previously (before Phase 1b's tenant-scoped uniqueness extended to
      admin accounts) it would have been rejected as "username already taken".
- [ ] Log in with `admin@acmetest.example` / `AcmeTest@2026!` (Acme's real password).
      You should land in **Acme Test Org** - check the Admins list or dashboard
      counts to confirm (Acme should still show 0 staff from a fresh tenant's
      perspective, or whatever you registered in section 4).
- [ ] Log out, log back in with `admin@acmetest.example` / `OtherOrgPassword@2026!`
      (the other tenant's password). You should land in the **other**, newly created
      tenant - a completely different Admins list / dashboard.
- [ ] Try logging in with `admin@acmetest.example` and a **wrong** password entirely.
      You should get a plain "Invalid login attempt" - not an error page, not a
      lockout on the first attempt.
- [ ] Clean up: delete the throwaway duplicate-email tenant from this section via
      `sqlcmd`/SSMS once you're done (same cleanup pattern as section 9).

**Not covered by this walkthrough:** `ForgotPassword`/`ResetPassword` also needed
fixing for this (see `multi-tenancy.md` / the fix commit), but exercising them for
real sends an actual email through `EmailService.cs`'s SMTP credentials - don't
trigger that in a routine test pass. If you need to verify that flow specifically,
do it deliberately and be aware it sends a real email.

## 6. Invoicing (`/Platform/Invoices`)

Log in as `admin@super.org`.

- [ ] Go to **Invoices**. Acme Test Org should appear in the "Generate Invoice"
      tenant dropdown.
- [ ] Select Acme Test Org and click **Generate for Current Period**. A new invoice
      (`INV-######`) should appear, status **Issued**, amount `29.99 USD`, due 14
      days out.
- [ ] Click **Generate for Current Period** again for the same tenant - no second
      invoice should be created (idempotent per period).
- [ ] Click **PDF** on the new invoice - downloads a PDF billed **from** Inprem (the
      platform operator, per `appsettings.json`'s `Platform:*` keys) **to** Acme Test
      Org.
- [ ] Click **Mark Paid**, fill in a date/method/reference, confirm - status flips to
      **Paid**.
- [ ] Generate one more invoice for a *different* tenant (or the same one again after
      manually changing its subscription period dates), then **Void** it instead -
      status flips to **Void**, and a `GenerateInvoiceAsync` call for that same
      period afterward would create a fresh one (voided invoices don't block
      regeneration).
- [ ] Visit Acme Test Org's detail page (`/Platform/Tenants/9`) - its invoice history
      should now show the invoice(s) you just created, read-only, with working PDF
      links.

## 7. Renewal & suspension sweep

The daily background job (`BillingBackgroundService`) runs automatically once at
every app startup, then every 24 hours - there's no button to trigger it on demand,
so testing it requires nudging data via `sqlcmd`/SSMS and restarting the app.

**Renewal**
- [ ] `UPDATE Subscriptions SET CurrentPeriodEnd = DATEADD(day, 5, SYSUTCDATETIME()) WHERE TenantId = 9;`
      (pulls Acme's period end inside the 14-day renewal window).
- [ ] Restart the app. Console output should log something like
      `Billing sweep complete: 1 subscription(s) renewed, ...`.
- [ ] Confirm: Acme's `CurrentPeriodStart`/`CurrentPeriodEnd` advanced by one month,
      and a new `Issued` invoice exists for that new period.
- [ ] Restart the app again without changing anything - the sweep should report
      `0 renewed` this time (no double-renewal).

**Past due → suspended**
- [ ] Pick one of Acme's `Issued` invoices and
      `UPDATE Invoices SET DueDate = DATEADD(day, -1, SYSUTCDATETIME()) WHERE Id = <id>;`
      (1 day overdue - within the grace period).
- [ ] Restart the app. Log should show `1 marked past due`. Confirm the invoice
      flipped to `Overdue` and the subscription to `PastDue`.
- [ ] `UPDATE Invoices SET DueDate = DATEADD(day, -30, SYSUTCDATETIME()) WHERE Id = <id>;`
      (now 30 days overdue - past the 14-day grace period).
- [ ] Restart the app. Log should show `1 suspended`. Confirm the subscription is now
      `Suspended`. (`Tenant.IsActive` is **not** touched by this - real enforcement
      is multi-tenancy.md Phase 9, not yet built.)
- [ ] Go to `/Platform/Invoices` and **Mark Paid** on that invoice. Confirm the
      subscription flips back to `Active` automatically.

---

## 8. Cross-cutting checks

- [ ] Browser console on any `/Platform/*` page shows no JavaScript errors.
- [ ] Every PDF generated anywhere in this guide opens correctly and shows the
      *correct* tenant's name in the header - if you ever see "Inprem Holistic
      Community Resource Center" on a document for Acme Test Org (or vice versa),
      that's a branding-leak regression (see multi-tenancy.md Phase 5).

## 9. Resetting between test passes

Recreate Acme Test Org from scratch if it gets into a confusing state:

```sql
SET QUOTED_IDENTIFIER ON;
DECLARE @Tid INT = (SELECT Id FROM Tenants WHERE Name = 'Acme Test Org');
DELETE FROM Invoices WHERE TenantId = @Tid;
DELETE FROM Subscriptions WHERE TenantId = @Tid;
DELETE FROM Staffs WHERE TenantId = @Tid;
DELETE FROM Volunteers WHERE TenantId = @Tid;
DELETE FROM AspNetUserRoles WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE TenantId = @Tid);
DELETE FROM AspNetUsers WHERE TenantId = @Tid;
DELETE FROM Tenants WHERE Id = @Tid;
```

Then recreate it through `/Platform/Tenants` as `admin@super.org` (name "Acme Test
Org", timezone `America/Chicago`, admin `admin@acmetest.example` /
`AcmeTest@2026!`), and re-set its subscription to `29.99`/`Monthly`/`Active` via its
detail page, to match the state this guide assumes at the start of each section.

If `admin@super.org` itself is ever missing (e.g. a fresh database), it's seeded at
startup from the `SeedSuperAdmin:Email`/`SeedSuperAdmin:Password` configuration keys
(`dotnet user-secrets`, not checked into source) - set those and restart the app once
to recreate it.
