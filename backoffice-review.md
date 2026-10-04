# Backoffice Review: Reference App vs. Our Implementation

**Date:** 2026-10-03
**Reference reviewed:** https://inpremclockin.org/backoffice (live production, read-only walkthrough — no records created, edited, or deleted)
**Compared against:** `InpremClockingApp` on `feature/multi-tenancy`

This is a feature inventory of the reference app's backoffice, with notes on what we already have, what we've already gone beyond, and what we're genuinely missing. Nothing was submitted, saved, or downloaded during the review; all screens were reached via read-only navigation (list → Details/Edit view, Back to List). With the client's permission, the session was also logged out and back in once to directly test login/access-control behavior — see "Login route" below and the companion `kiosk-review.md` for the full findings.

## Login route

The reference app has exactly **one** login page (`/Identity/Account/Login`), and it's permanently headed **"Admin Log in only"** — confirmed identical whether visited bare or with a `?ReturnUrl=` pointing at a kiosk destination. There is no kiosk-styled variant. This is the direct counterpart to the Logout bug we fixed for our own app this session (where our shared login page briefly rendered the wrong style — kiosk vs. Back Office — depending on where the user came from): the reference app never solved that problem at all; it simply always shows the admin-branded screen to anyone redirected there, including a volunteer whose session lapsed. We're ahead here, not behind.

Access-control testing while logged out also surfaced a real inconsistency in the reference app: `/backoffice` and `/Main/Index` (Volunteer kiosk entry) correctly require login, but `/Staff/Index` (Staff kiosk entry) and `/Volunteer/Clocking?VolunteerId={id}` (the clock-action screen for an existing volunteer) do **not** — they load fully for an anonymous visitor. This reads like a missing `[Authorize]` attribute on their end rather than an intentional design, and isn't something to replicate. Full detail and a route-by-route table are in `kiosk-review.md`.

## Overall structure

The reference app's backoffice sidebar has exactly five items: **Dashboard, Volunteer Clockings, Staff Clockings, Manage Volunteers, Manage Staff**. There is no Admins page, no Reports submenu, and (expectedly, since it's a single-organization deployment) nothing resembling our multi-tenancy/Platform/Billing area. It is built on ASP.NET Core Identity with the same `/Identity/Account/...` routes we use, which confirms our architecture choice lines up with theirs — but it uses classic MVC controller actions (`/ManageVolunteers/Edit?id=X`, `/ManageClockings/Details?id=X`, etc.) rather than Razor Pages.

**Where we're already ahead** (not gaps — call out for context, not action):
- Multi-tenancy, Platform/Tenant management, Subscriptions/Invoices/Billing — none of this exists in the reference at all.
- A dedicated Admins management page (`/User`, "Create Admin Account") — the reference only shows an admin *count* on the Dashboard, with no way to view/manage who they are.
- The entire volunteer-category questionnaire (Mandated Community Hours / MOFC / Educational Purposes / Corporate Volunteering / Personal, plus the conditional Mandate Type, Institution Name, Place of Work, Contact Person, and the Educational-Purposes-only Contact Person Position/Email/Phone) — **the reference's "Add New Volunteer" form has none of this.** It's a bare form: Email, First Name, Last Name, ZipCode, Gender (free-text, not a dropdown), Type (free-text), Phone Number, Address, and — only for volunteers — a raw **Password** field. This confirms the questionnaire was a requirement added directly to us, not something we copied from (or need to catch up to in) the reference.
- Per-record "Hours" report link for **Staff**, not just Volunteers — the reference only offers this quick link on the Volunteer list; Staff only get Edit/Details. We already added Staff parity here ourselves.

## Dashboard

Reference Dashboard shows:
- Overall total Volunteers, overall Total Working Time (all-time), and "Total number of Volunteers who worked for selected Period" driven by an inline date-range picker.
- An inline **"Calculate Total Hours"** button that recomputes that selected-period volunteer count without leaving the page, and a **"Download Report"** button next to it (org-wide PDF for the selected date range — not scoped to one person).
- Separate cards for Total Staff + Total Time, and Total Admins.

**Gap:** we don't have an org-wide, date-range-scoped "download a combined report" action on the Dashboard. Our PDF generation (`StaffReportsController`/`VolunteerReportsController`) is per-person only, reached via a "Hours" link on an individual record. If the client wants "give me a report for everyone who worked in March," that capability doesn't currently exist for us outside of per-person requests.

## Volunteer Clockings / Staff Clockings (raw clocking records)

These are plain server-paginated tables (fixed at 10 rows/page, no page-size selector, no search box) listing every historical clocking record across all people. Columns: Full Name, Clock In Time, Clock Out Time, Leave On Break Time, Return from Break Time, Working Hours (raw, e.g. `00:21:08.3110000` for staff), and (staff only) a separate Date column. Action column is `Edit | Details`.

- **"Add New Clock in"** is a bare form requiring the admin to type a raw numeric `VolunteerId`/`StaffId` and manually fill in every timestamp field (Clock In, Clock Out, Leave on Break, Return from Break), a free-text Working Hours value, and a Created Date — no name search, no current-time defaulting, no computed hours. Our "Manual Clocking Here" form (search-by-name-or-email, auto-filled current time) is a meaningfully better UX here — not a gap.
- **"Edit" is a full raw-field editor** for an *existing* clocking record: every timestamp, the Working Hours value, the Created Date, and even the owning Volunteer/Staff ID are directly editable.
- **"Details"** is a simple read-only field dump of the same raw record.

**Gap (the most significant one in this area):** we have no way to correct an existing, already-completed clocking record. Our only mutation paths on `StaffClocking.cshtml`/`VolunteerClocking.cshtml` are the quick-action buttons (Clock Out / Break Start / Break End, which only act on *today's open* record) and the manual clock-in creation form. If a volunteer's break time was logged wrong last Tuesday, or an admin needs to fix a typo'd clock-out time from last month, there is currently no UI for that in our app — the reference app's blunt "Edit any field of any past record" capability, primitive as it is, covers a real admin need we don't.

## Manage Volunteers / Manage Staff

These lists are DataTables-powered: a page-size selector (10/25/50/100 — matches ours), a free-text search box that searches across all visible columns, **and built-in export buttons: Copy, CSV, Excel, PDF, Print** (plus an email icon). Columns: Email Address, First Name, Last Name, ZipCode, Gender, Type, Phone Number, Address, Action. Volunteers get `Edit | Details | Hours`; Staff get `Edit | Details` only.

**Gap:** our `Staff.cshtml`/`Volunteer.cshtml` list pages have no export capability at all — no CSV, Excel, PDF, Print, or Copy. This is a concrete, easy-to-name feature gap worth prioritizing if the client relies on pulling volunteer/staff rosters into a spreadsheet or printing them.

**Minor gap:** the reference provides a separate, pure read-only **Details** page per Staff/Volunteer record, distinct from Edit. We conflate the two — our "Edit" modal is the only way to view a record's full fields, pre-filled and immediately editable. Not critical, but worth considering if there's ever a need for a "view only, don't let me accidentally change something" mode (e.g., for a lower-privilege future role).

### Per-volunteer "Hours Worked" report (the "Hours" link)

This lands on a dedicated, letter-style page: *"This is a confirmation email of volunteer hours worked by **[Name]** at Inprem Holistic Community Resource Center, 5757 Karl Rd, Columbus Ohio,"* followed by a sortable/searchable/paginated table of every clocking record for that one person, a running total ("Total: 02 hour(s) 59 minutes"), a closing note ("Please contact us for further information. Inprem Admin"), and a date-range picker + "Download as PDF" button scoped to that range.

This corresponds closely to our `OneVolunteerClockingReport`/`OneStaffClockingReport` pages. **Action item, not a functional gap:** worth double-checking our page uses the same org address, the same confirmation-letter framing and closing note, and that our PDF is also scoped by an adjustable date range — for consistency of what external parties (schools, courts, employers) actually receive, since that's the artifact this app exists to produce.

## Bugs observed in the reference app (informational only — not things to replicate)

- `/Identity/Account/Manage` (the account/profile page, reached via the "Manage" link next to the signed-in user's email) throws an unhandled server error (HTTP 500) in production.
- The kiosk-side Staff search (`/Staff/SearchStaff`) also throws a server error (HTTP 500) when submitted — see the kiosk review for detail. Our equivalent (`/api/search/*`) already works correctly for both Staff and Volunteer, so this is not something we need to fix, just something worth knowing is broken on their end if the client ever compares the two side by side.

## Summary table

| Area | Reference has it | We have it | Note |
|---|---|---|---|
| Edit a past/completed clocking record | Yes (raw field editor) | **No** | Real gap — flagged above |
| Export Manage Staff/Volunteers list (CSV/Excel/PDF/Print) | Yes | **No** | Real gap — flagged above |
| Org-wide date-range report from Dashboard | Yes | **No** | Real gap — flagged above |
| Separate read-only "Details" view vs. Edit | Yes | No (Edit-only modal) | Minor, optional |
| Admins management UI | No (count only) | **Yes** | We're ahead |
| Multi-tenancy / Platform / Billing | No | **Yes** | We're ahead |
| Volunteer category questionnaire | No | **Yes** | We're ahead (and reference confirms this wasn't theirs to begin with) |
| Per-staff "Hours" quick link | No | **Yes** | We're ahead |
| Manual clock-in UX (search-by-name, time defaults) | No (raw ID entry) | **Yes** | We're ahead |
