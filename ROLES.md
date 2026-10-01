# Roles & Permissions

This describes every account type that actually exists in the app today (`feature/multi-tenancy`
branch), what each one can and can't do, and where that's enforced in code. Two real roles
exist — **SuperAdmin** and **Admin** — plus two things that are *not* separate roles but are
easy to mistake for one: a "kiosk" mode of use, and an anonymous visitor.

## Summary

| | SuperAdmin (Provider) | Admin (Tenant Admin) | Kiosk sign-in | Anonymous |
|---|---|---|---|---|
| A real ASP.NET Identity role? | Yes (`SuperAdmin`) | Yes (`Admin`) | No — same account as Admin | No account |
| Scoped to a tenant? | No (`TenantId` always `null`) | Yes, strictly | Yes (inherits the Admin's tenant) | N/A |
| Can manage staff/volunteers/clockings? | No | Yes, own tenant only | Yes (sign-up + clock-in/out only) | No |
| Can create tenants? | Yes | No | No | No |
| Can create Admin accounts? | Only as part of creating a new tenant | Yes, for their own tenant only | No | No |

## 1. SuperAdmin (Provider)

The platform operator. Not tied to any organization using the app — exists to onboard new
tenants, not to run one day-to-day.

**How the account is created**
- Manually, via the `SeedSuperAdmin:Email` / `SeedSuperAdmin:Password` configuration keys
  (`IdentitySeeder.SeedSuperAdminAsync`, runs at every startup, does nothing once that email
  already exists) — or by directly adding a row to `AspNetUserRoles` linking an existing
  account to the `SuperAdmin` role.
- There is no self-serve or in-app way to become a SuperAdmin. This is intentional.

**Tenant scoping**: `AppUser.TenantId` is always left `null` for this role. A SuperAdmin
doesn't belong to any organization's data.

**Can do**
- Sign in and reach `/Platform/Tenants` (gated by the `SuperAdminOnly` policy —
  `RequireRole("SuperAdmin")`).
- Create a new tenant (organization name, IANA timezone, address/contact info) together with
  its first Admin account, in one submission (`TenantAdminService.CreateTenantWithAdminAsync`).
  This is the *only* place in the app that creates a `Tenant` row or sets an `AppUser.TenantId`
  to something other than the acting user's own tenant.
- See the full list of every tenant that exists (`TenantAdminService.GetAllTenantsAsync`,
  deliberately bypasses the tenant query filter via `IgnoreQueryFilters()` — the one audited
  place in the codebase that does this for this purpose).
- The sidebar shows a **Tenants** link only when `User.IsInRole("SuperAdmin")` is true.

**Cannot do (restrictions)**
- Cannot reach any `AdminOnly`-gated page — Dashboard, Manage Staff/Volunteers, Admins,
  Settings, Staff/Volunteer Clocking, Hours Worked, or any Reports page. These all require the
  `Admin` role specifically; having `SuperAdmin` does not satisfy that check. A SuperAdmin who
  navigates to e.g. `/BackOffice` gets Access Denied, the same as anyone else without the
  `Admin` role.
- Cannot delete a tenant outright, or reset a tenant admin's password — there's no UI for
  either yet. (Viewing/editing an existing tenant's details, force-activating/suspending it, and
  seeing which admins belong to it *are* now possible, via `/Platform/Tenants/{id}` — see
  multi-tenancy.md Part 2, Phase 7.)
- Cannot see or touch any tenant's Staff/Volunteer/Clocking data, even though they technically
  could look at the `Tenants` table row for that org — the query filter on every business table
  still requires a matching `TenantId`, which a SuperAdmin's session never has.
- Not exempt from the same walk-up "kiosk" pages either, in the sense that using them would be
  meaningless: a SuperAdmin has no `TenantId`, so if they tried to register a staff member via
  `/StaffAttendance`, the new row would need a tenant to be stamped into automatically — since
  `ApplicationDbContext`'s auto-stamp only fires when the signed-in user *has* a `TenantId`, a
  SuperAdmin doing this would need a tenant supplied some other way. In practice, nothing in the
  UI is built for a SuperAdmin to use these pages at all.

## 2. Admin (Tenant Administrator)

The role every real user of the app has today (including Inprem's own staff). Full back-office
access, but strictly confined to their own organization's data.

**How the account is created**
- The first Admin of a brand-new tenant is created together with the tenant itself, by a
  SuperAdmin, via `/Platform/Tenants`.
- Additional Admins for an *existing* tenant are created by another Admin of that same tenant,
  via `/User` ("Create Admin Account") — gated by `AdminOnly`, and by the fact that
  `/Identity/Account/Register` (the scaffolded Identity sign-up page) is itself locked to
  `AdminOnly` too, so there is no public self-registration path for admin accounts at all.
- Either way, `AppUser.TenantId` is set once at creation and never changes: for admins created
  via `/User`, it's auto-stamped to the *creating* Admin's own tenant
  (`ApplicationDbContext.SaveChanges` override) — an Admin can never accidentally or
  deliberately create an account for a different tenant.

**Tenant scoping**: strict. Every query against `Staff`, `Volunteer`, `ClockingStaff`,
`Clocking`, `Setting`, and `Tenant` is automatically filtered to the signed-in Admin's own
`TenantId` by an EF Core global query filter (`ApplicationDbContext.OnModelCreating`) — this
isn't a per-page check that a developer could forget to add, it applies to every query in the
app by construction. Guessing another tenant's Staff/Volunteer/Clocking ID directly by URL
returns "not found," never that tenant's real data (verified in `multi-tenancy.md`'s Phase 2/5
testing).

**Can do**
- Everything under the main sidebar for their own tenant: Dashboard, Manage Staff, Manage
  Volunteers, Staff/Volunteer Clockings (manual clock in/out/break actions), Admins (create more
  Admins for their own tenant), Logout Settings, Hours Worked, and every Reports page (Staff
  List, Volunteer List, Staff/Volunteer Clocking Report, PDF downloads) — see the permissions
  table below for the exact page list.
- Use the kiosk pages (sign-up + clock-in/out) exactly as described in section 3 below, since
  those only require *any* authenticated account, which an Admin satisfies.

**Cannot do (restrictions)**
- Cannot reach `/Platform/Tenants` — that page requires the `SuperAdmin` role specifically;
  being an Admin never satisfies a `SuperAdminOnly` check. The **Tenants** sidebar link doesn't
  even render for an Admin.
- Cannot create a new tenant, or an Admin account for any tenant other than their own.
- Cannot see another tenant's staff, volunteers, clockings, settings, or admin list, under any
  circumstances — enforced at the data layer, not just hidden in the UI.
- Two different tenants *can* register a person with the same email address (this is allowed,
  intentionally) — but within their own tenant, an Admin still cannot create a duplicate
  Staff/Volunteer email or username; that's enforced by a tenant-scoped unique index.

## 3. Kiosk sign-in (not a separate role)

This is a **mode of use**, not a role — there is no "Kiosk" entry in `AspNetRoles`. The pages
meant to run on a walk-up device (`/StaffAttendance`, `/VolunteerAttendance`,
`/staff-clockin/{id}`, `/volunteer-clockin/{id}`) are gated with a plain `[Authorize]` attribute
and nothing more — no `AdminOnly` or `SuperAdminOnly` policy. That means *any* signed-in
account, Admin or SuperAdmin, satisfies the check.

In practice this is always an Admin account, because:
- Every account-creation path in the app (seeding, `/User`, `/Platform/Tenants`) only ever
  produces an `Admin` or `SuperAdmin` account — there's no third, lower-privileged account type
  to log a kiosk device in as.
- A physical kiosk device is expected to stay signed in as one tenant's Admin account
  indefinitely; whatever staff/volunteers it registers or clocks in get stamped with that
  Admin's own `TenantId` automatically, with nobody at the device ever seeing or choosing a
  tenant.

So "kiosk access" isn't a smaller set of permissions than Admin — it's simply the subset of an
Admin's permissions that doesn't require the stricter `AdminOnly` policy, made available this
way so a walk-up device's session doesn't need to be treated any differently from a back-office
session.

## 4. Anonymous (not signed in)

- Every Razor Page in the app requires authentication one way or another — there is no public
  page. An anonymous visitor hitting any URL is redirected to `/Identity/Account/Login`.
- Self-registration is closed (see Admin section above) — an anonymous visitor cannot create
  their own account under any circumstances. Only an existing Admin or SuperAdmin can provision
  a new account.

## Full page/route permissions table

| Page / route | Requires |
|---|---|
| `/BackOffice`, `/Staff`, `/Volunteer`, `/User`, `/Settings`, `/StaffClocking`, `/VolunteerClocking`, `/StaffReport`, `/StaffClockingReport`, `/VolunteerReport`, `/VolunteerClockingReport`, `/HoursWorked` | `AdminOnly` (role `Admin`) |
| `/Identity/Account/Register` | `AdminOnly` (role `Admin`) |
| `/Platform/Tenants`, `/Platform/Tenants/{id}` | `SuperAdminOnly` (role `SuperAdmin`) — gated at the folder level (`AuthorizeFolder("/Platform", ...)`), so any future `/Platform/*` page is covered automatically |
| `/StaffAttendance`, `/VolunteerAttendance`, `/staff-clockin/{id}`, `/volunteer-clockin/{id}`, `/Index`, `/Privacy` | `[Authorize]` only — any signed-in account |
| Everything else under `/Identity/Account/...` (Login, Logout, forgot-password, 2FA, manage-account pages) | Identity's own built-in rules (e.g. Login/Logout/ForgotPassword are open to anonymous by necessity; Manage pages require being signed in as whoever they belong to) |

The API controllers behind these pages now carry matching `[Authorize]` attributes of their
own (fixed - see "Known gaps" history below), rather than relying solely on the Razor Page
gate:

| Controller | Requires | Matches |
|---|---|---|
| `ControlsController` (`api/controls/*` - clock in/out/break) | `[Authorize]` | `StaffClockPage`/`VolunteerClockPage` |
| `SearchController` (`api/search/*`) | `[Authorize]` | `StaffAttendance`/`VolunteerAttendance` |
| `PeopleController` (`api/people/emails`) | `AdminOnly` | `Staff`/`Volunteer`/`StaffReport`/`VolunteerReport` |
| `Api.PeopleController` (`api/people/staff`, `api/people/volunteers`) | `AdminOnly` | `StaffClocking`/`VolunteerClocking`/clocking reports |
| `Api.StaffReportsController`, `Api.VolunteerReportsController` (`api/people/*hours*`, `*-pdf`) | `AdminOnly` | `HoursWorked`, clocking reports, list reports |
| `ReportsController`, `StaffController` (`api/reports/*`, `api/staff/*`) | `AdminOnly` | Not called by any current page, but the routes are live - gated the same as the back-office functionality they're shaped for |

## Known gaps (history)

- ~~**The API controllers have no `[Authorize]` of their own.**~~ **Fixed.** Every controller
  now carries an `[Authorize]` attribute matching the Razor Pages that call it (table above) -
  `ControlsController`/`SearchController` get plain `[Authorize]` (matching their kiosk-page
  callers), everything else gets `AdminOnly`. Verified: unauthenticated requests to every
  endpoint now redirect to login instead of executing; a SuperAdmin (who has no `Admin` role)
  is correctly turned away from `AdminOnly` endpoints; legitimate Admin access is unaffected.
- **No middle tier between Admin and SuperAdmin.** Anyone who can sign in as an Admin has full
  read/write access to everything in their tenant — there's no "front-desk-only" or read-only
  role for someone who should only be allowed to use the kiosk pages, not Manage Staff/Settings/
  Reports.
- ~~**SuperAdmin tooling is create-and-list only.**~~ **Mostly fixed.** `/Platform/Tenants/{id}`
  (multi-tenancy.md Part 2, Phase 7) now supports editing a tenant's name/timezone/address/
  contact, force-activating/suspending it, viewing its admins, and viewing/editing its
  subscription. Still missing: deleting a tenant outright, and resetting a tenant admin's
  password.
