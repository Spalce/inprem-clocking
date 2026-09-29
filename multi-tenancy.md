# Multi-Tenancy Implementation Plan

## Status (2026-09-29)

Phases 0 through 4 are done, committed on `feature/multi-tenancy`, and verified end-to-end -
including creating a real second tenant entirely through the UI (SuperAdmin onboarding ->
tenant admin login -> staff registration -> clock in/out -> report), confirming isolation and
per-tenant timezone resolution both hold with no visibility into Inprem's data at any step.
Data isolation and tenant onboarding are both live now, not just planned. Only Phase 5
(hardening/docs pass) remains - see "What you'll need to handle" below for the checkpoint
before continuing.

## Goal

Turn InpremClockingApp from a single-organization app (hardcoded to Inprem Holistic
Community Resource Center) into a multi-tenant system where multiple organizations can use
the same deployment, each with its own staff, volunteers, admins, clockings, reports, and
settings — completely isolated from every other organization's data.

This is a **data and architecture change**, not a presentation change: it touches the schema,
every query in the app, and authentication/authorization. It's also a retrofit onto a system
with real, live data (Inprem's own records), not a green-field feature — so the guardrails
below are stricter than the Tailwind UI work.

## Branch

All work happens on `feature/multi-tenancy`, created off `main`. Nothing merges to `main`
until every phase is complete, verified, and explicitly reviewed by you. Nothing is pushed to
`origin` or merged by me.

## Guardrails (apply to every phase, no exceptions)

- **Back up the database before any migration that touches existing rows.** Unlike the earlier
  full data wipe (which you explicitly asked for, on throwaway test data), the data this plan
  operates on starts out as Inprem's real records — every schema change that backfills or
  reshapes existing rows gets a `BACKUP DATABASE` step first, no exceptions.
- Inprem's own data must keep working, unchanged, as the first tenant throughout. At no point
  should Inprem staff/admins see broken pages, lost data, or altered behavior mid-rollout.
- Every phase ends with a real `dotnet ef migrations add` + `dotnet ef database update` +
  a live build-and-click-through verification before moving to the next phase — the same
  discipline as `TESTING.md`, extended to cover tenant isolation specifically once that exists.
- No phase ships tenant *data* isolation without an accompanying isolation *test*: log in as
  one tenant's admin and confirm another tenant's records are invisible, both through the UI
  and by guessing IDs directly in the URL (e.g. `/OneStaffClockingReport`-style ID tampering,
  even though that specific page was removed — any page or API route taking a raw ID must be
  re-checked).
- This plan folds in the timezone redesign already agreed on separately (see
  [[multitenancy_timezone_plan]] in project memory) — it is not being re-derived here, just
  scheduled into a phase.

## Key architectural decisions

### 1. Shared database, row-level isolation via `TenantId` (recommended)

Three standard options exist for multi-tenant data isolation: a separate database per tenant,
a separate schema per tenant, or one shared database/schema with a `TenantId` column on every
table ("row-level" isolation). For this app's scale — a handful to a few dozen small
organizations, not thousands of enterprise customers — **shared database with row-level
isolation** is the right default: it's the cheapest to run and operate (one database to back
up, migrate, and monitor), and EF Core's **global query filters** (see decision #3) give it
strong isolation guarantees without per-tenant infrastructure. Database-per-tenant is worth
revisiting only if a future customer has a hard compliance requirement for physically separate
storage — not needed to start.

### 2. New `Tenant` entity, `TenantId` added to every business table

A new `Tenants` table: `Id`, `Name`, `TimeZoneId` (IANA, see decision #4), `IsActive`,
`CreatedAt`.

`TenantId` gets added directly to `Staff`, `Volunteer`, `ClockingStaff`, `Clocking`,
`Setting`, and `AspNetUsers` (via `AppUser`) — not just the "root" entities. Denormalizing it
onto the clocking tables too (rather than relying on a join through `Staff`/`Volunteer`) means
every tenant-scoped table can carry its own query filter independently, so there's no code path
where an `Include()` or a raw query accidentally bypasses isolation by joining through an
unfiltered table.

### 3. EF Core global query filters — the actual isolation mechanism

`ApplicationDbContext` gets a scoped `ICurrentTenantService` injected via constructor, and a
`HasQueryFilter(e => e.TenantId == _currentTenant.TenantId)` on every tenant-scoped entity in
`OnModelCreating`. This is the single most important property of the whole design: isolation
is enforced by the ORM by default, everywhere, automatically — a future developer adding a new
`_db.Staffs.Where(...)` call in some new controller cannot accidentally leak another tenant's
rows, because the filter applies before their `Where` even runs. Existing code (`FindAsync`,
`AnyAsync`, `FirstOrDefaultAsync`, etc. across `StaffService`, `VolunteerService`,
`StaffClockingService`, `VolunteerClockingService`, `ControlsController`, both
`PeopleController` classes, both report controllers) needs **no manual tenant-filtering
changes** once this is in place — the filter is invisible to that code.

A small, explicitly-audited set of SuperAdmin-only pages (tenant management itself, decision
#6) bypass the filter deliberately via `IgnoreQueryFilters()`, and only there.

### 4. Timezone becomes tenant data (folds in the earlier-agreed plan)

`Tenant.TimeZoneId` stores an IANA id (`"America/New_York"`, not `"Eastern Standard Time"`) —
portable across OS, resolvable by .NET 6+ on both Windows and Linux. The static `OrgClock`
class is replaced by a scoped `ITenantClock` service that reads `ICurrentTenantService` to find
the current request's tenant and its timezone. Every `OrgClock.NowLocal()` / `OrgClock.ToUtc()`
/ `OrgClock.ToLocal()` call site (`StaffClockingService`, `VolunteerClockingService`,
`ControlsController`, `HoursWorked.cshtml.cs`, the kiosk pages, report controllers) switches to
injecting `ITenantClock` instead. This stays strictly about *authoritative* business time
(`ClockDate`, the one-session-per-day rule, PDF report timestamps) — an admin's personal
display-timezone preference, if ever added, is a separate client-side concern layered on top.

### 5. Tenant resolution: claim on the signed-in user (decided 2026-09-29 — no subdomain, for now)

Every page in this app that touches tenant-scoped data already requires a signed-in
`AppUser` (`[Authorize]`, enforced everywhere from the kiosk clock pages up to the back
office). So instead of routing on subdomain, the tenant is resolved from **the signed-in
user's own account**: `AppUser.TenantId` is stamped into the authentication cookie as a claim
at sign-in time (via a custom `IUserClaimsPrincipalFactory`), and `ICurrentTenantService` reads
that claim on every request. No DNS, no wildcard SSL, no hosting changes — this works
unchanged on the current SmarterASP.NET deployment.

This means a kiosk device's tenant is simply whichever `AppUser` it's logged in as — exactly
how it works today (implicitly, for the one existing tenant), just backed by a real `TenantId`
now instead of there being only one possible answer.

Subdomain-based resolution (as originally proposed) is deliberately not implemented now, but
nothing else in this plan depends on today's choice: `ICurrentTenantService` is the only thing
that would need to change if a subdomain (or any other) resolution strategy is adopted later —
every service, controller, and query filter downstream of it stays untouched.

### 6. Roles: "Admin" stays per-tenant, new "SuperAdmin" for platform operators

ASP.NET Core Identity roles are global by name, but since every `AppUser` carries a `TenantId`,
existing `[Authorize(Policy = "AdminOnly")]` checks keep working unmodified — "Admin" continues
to mean "admin of their own tenant," enforced together with (not instead of) the query filter.
A new `SuperAdmin` role is added for the handful of people (you) who create and manage tenants
themselves, via a small new back-office area outside the tenant-filtered pages.

## Migration strategy for existing data

Inprem's current data has no `TenantId` at all. The rollout:

1. Add `TenantId` columns as **nullable**, everywhere, with no query filters yet and no
   behavior change — this is a no-op migration from the app's perspective.
2. Insert one `Tenant` row for Inprem itself, backfill every existing row's `TenantId` to it.
3. Make the columns **non-nullable**, add the foreign keys, and replace the existing
   `EmailAddress`-unique and `(StafId/VoluntId, ClockDate)`-unique indexes with tenant-scoped
   composite versions (`(TenantId, EmailAddress)`, `(TenantId, StafId, ClockDate)`) — uniqueness
   should hold per-tenant, not globally, since two different organizations may happen to
   register a volunteer with the same email address.
4. Only then turn on the query filters (decision #3) — by this point every row already has a
   correct `TenantId`, so enabling the filter changes nothing observable for Inprem.

This mirrors the caution already applied to `scripts/convert-existing-timestamps-to-utc.sql`:
schema/semantic changes on top of accumulated real data get backfilled and verified in
read-only queries before anything becomes a hard constraint.

## Phased plan

**Phase 0 — Foundation (no visible change)**
Add the `Tenant` entity and nullable `TenantId` columns to every business table and
`AspNetUsers`, via migration. No query filters, no resolution middleware, no behavior change.
Verify: app builds, runs, and behaves exactly as today; the new columns exist and are `NULL`
everywhere except never queried.

**Phase 1 — Backfill and tighten the schema**
Create the Inprem tenant row, backfill `TenantId` on all existing data, make columns
non-nullable, replace the global-unique indexes with tenant-scoped composite indexes.
*Back up the database before this phase.*
Verify: row counts unchanged, every row has the Inprem `TenantId`, existing unique-constraint
behavior (duplicate email/phone rejection, one-clocking-per-day) still works identically.

**Phase 2 — Tenant context plumbing and query filters**
Build `ICurrentTenantService`, the claims-based resolution (decision #5: `TenantId` claim set
at sign-in), wire it into `ApplicationDbContext`, and turn on the global query filters. This is
the phase where isolation actually starts being enforced.
Verify: every existing page/flow still works for Inprem; a throwaway second tenant's data
(created for testing) is completely invisible from Inprem's session, in the UI and via direct
ID guessing.

**Phase 3 — Timezone refactor**
`Tenant.TimeZoneId` (IANA), `ITenantClock` replacing the static `OrgClock`, every call site
updated. Set Inprem's `TimeZoneId` to the IANA equivalent of the current "Eastern Standard
Time" setting so its behavior is unchanged.
Verify: clock-in/out times, `ClockDate` day-boundary behavior, and PDF report timestamps are
identical to before this phase, for Inprem.

**Phase 4 — Tenant management**
SuperAdmin-only pages to create a new tenant (name, timezone) and its first admin account.
Update the existing Register/sign-up flows so anything a tenant's admin creates
(staff, volunteers, other admins) is automatically stamped with their own `TenantId` — never a
field the person filling out a form has to think about.
Verify: create a second real test tenant end-to-end (onboarding → admin login → register staff
→ clock in/out → PDF report) entirely through the UI, with zero visibility into Inprem's data
at any step.

**Phase 5 — Verification and hardening**
Full cross-tenant isolation pass: every page and API route that accepts an ID re-checked for
filter coverage, `TESTING.md` extended with a "multi-tenant isolation" section, side-by-side
review against current behavior for Inprem. Hand back for your review — no merge or push
without your explicit go-ahead.

## What I'll handle

- All schema, service, controller, and page changes for every phase, isolated on
  `feature/multi-tenancy`.
- Writing and running migrations, including the backfill scripts, with backups taken first.
- Functional and isolation verification after each phase, in-browser.
- Keeping this document updated with progress/status as phases complete.

## What you'll need to handle

- Confirm whether tenant onboarding is SuperAdmin-created only (Phase 4 as scoped) or needs to
  be self-serve signup — self-serve is a larger scope addition I'd want to plan separately if
  wanted.
- Review checkpoints — at minimum after Phase 1 (schema backfill, since it touches live data)
  and Phase 2 (isolation goes live). Tell me to continue, adjust, or stop.
- The final merge to `main` — I will not do this myself, per your standing instruction.
