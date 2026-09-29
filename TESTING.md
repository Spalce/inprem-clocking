# Manual Test Guide

A guided walkthrough for testing InpremClockingApp end-to-end, with emphasis on
everything touched during the Tailwind UI modernization (`UI-modification.md`)
and the fixes that followed it. Pair this with a fresh seed data load so every
list actually has enough rows to page through.

## 1. Setup

```bash
# from the repo root
dotnet build InpremClockingApp.sln
dotnet run --project InpremClockingApp
```

App runs at `https://localhost:7078` (and `http://localhost:5250`). Log in with
`admin@inprem.org` and your existing password.

### Load test data

```powershell
# wipes everything except the admin@inprem.org account, then loads a fresh batch
sqlcmd -S localhost -d DB_A65635_inpremdb -E -i scripts\reset-test-data.sql
sqlcmd -S localhost -d DB_A65635_inpremdb -E -i scripts\seed-test-data.sql
```

Adjust `-S`/`-d` if your connection string differs (check
`appsettings.json` → `ConnectionStrings:DefaultConnection`). Re-run both any
time you want a clean slate for another test pass - `reset-test-data.sql`
always keeps `admin@inprem.org` and the Logout Settings row, and never touches
anything outside this app's own tables.

### What the seed data contains

| Table | Rows | Notes |
|---|---|---|
| Staff | 25 | Unique emails like `james.smith.staff1@example.com` |
| Volunteers | 25 | Unique emails like `james.garcia.vol1@example.com`, spread across all 5 volunteer categories |
| Staff clockings | 45 | See breakdown below |
| Volunteer clockings | 45 | Same breakdown, mirrored |

Clocking record breakdown (same shape on both sides):
- **20 people** have two *complete* 8-hour shifts each, on different days over
  the last ~4 weeks - this is what fills up pagination and gives the date-range
  report filters something to filter.
- **4 people are still clocked in *today*, with no clock-out yet**:
  Staff: Daniel Lee, Nancy Perez, Matthew Thompson, Lisa White.
  Volunteers: Daniel Smith, Nancy Johnson, Matthew Williams, Lisa Brown.
  Use these to try the Actions dropdown (Break Start / Break End / Clock Out)
  without disturbing any historical data.
- **1 person per side was clocked in two days ago and never clocked out**:
  Staff: **Anthony Harris**. Volunteer: **Anthony Jones**.
  This deliberately reproduces the exact scenario that used to crash the app
  with a `SqlDbType.Time overflow` error - clocking them out now should
  succeed and show `23:59:59` (a capped fallback, not a real measurement,
  since the session was genuinely left open too long).

---

## 2. Login & Admin access

- [ ] Log in at `/Identity/Account/Login` with `admin@inprem.org`. Should land
      on `/BackOffice`.
- [ ] Log out via the top-right **Logout** link. Should land back on a clean
      login page with no stray console errors.
- [ ] While logged out, try navigating directly to `/BackOffice` - should
      redirect to Login (not "Access Denied", since you're not authenticated
      at all yet).
- [ ] **Admins** page (sidebar → Admins): create 2-3 throwaway admin accounts
      via the "Create Admin Account" form, to get the list past 4-5 rows and
      see its pagination controls render (default page size 20, so you'd need
      21+ to actually see multiple pages - optional, just to exercise the
      form + list).

## 3. Dashboard (`/BackOffice`)

- [ ] Staff/Volunteer/Admin total counts match what's in the database (25 / 25
      / however many admins you have).
- [ ] No redundant "Dashboard" link floating at the top-right next to the page
      title (this was removed - the page title itself is enough).
- [ ] Three summary cards (Staff/Volunteer/Admins) render with a colored top
      border and don't overlap each other or overflow their card boundary.

## 4. Manage Staff / Manage Volunteers

Repeat this whole section for both **Manage Staff** and **Manage Volunteers**
(they're mirror-image pages).

**List & search**
- [ ] All 25 rows are visible across pages; no column text overlaps another
      column or the Actions buttons.
- [ ] Search box and the "Search" button next to it don't overlap - there
      should be a clean gap between them at every viewport width you try.
- [ ] Type a partial name (e.g. "Smith") into the search box - the hint line
      below it should suggest matching emails; click one to filter to just
      that record.
- [ ] Clear the search and confirm the full list comes back.

**Pagination**
- [ ] Change "Page size" to 10 - should show 3 pages. Change to 50 - should
      show all 25 on one page.
- [ ] Click through Previous/Next and specific page numbers. No stray bullet
      dots (•) should appear next to Previous/page-numbers/Next - if you see
      any, that's the list-style regression coming back.
- [ ] Previous is disabled/greyed on page 1, Next is disabled/greyed on the
      last page.

**Add / Edit modal**
- [ ] Click **Add New** - modal opens centered with a dimmed backdrop.
- [ ] Close it three different ways and confirm each works: the **×** button,
      clicking the dimmed backdrop *outside* the modal panel, and pressing
      **Escape**.
- [ ] Click **Edit** on an existing row - modal opens pre-filled with that
      row's data.
- [ ] Clear the Address field (or type fewer than 10 characters) and Save -
      should show a validation error inside the modal, not crash or silently
      fail.
- [ ] Fix the address and Save again - modal closes, page reloads, the edited
      value shows in the table.

**Move action**
- [ ] Click **Move To Volunteer** (or **Move To Staff**) on a seeded row,
      confirm the browser confirm-dialog, and verify the record disappears
      from this list and appears on the other one.

**Hours link**
- [ ] Click **Hours** on any row - lands on `/HoursWorked?type=staff&id=...`
      (or `type=volunteer`). See section 6 below for what to check there.

## 5. Staff Clocking / Volunteer Clocking

Repeat for both **Staff Clockings** and **Volunteer Clockings**.

**Manual clocking**
- [ ] Type a few letters of a seeded person's name into "Search Staff (name or
      email)" - a dropdown of matches appears; click one to select it.
- [ ] Try submitting without picking a match from the dropdown - should be
      blocked with a validation message, not silently submit with an empty
      selection.
- [ ] Pick someone who does *not* already have an open session today, leave
      Clock In at its pre-filled current time, and submit - a new row appears
      at the top of the table.

**Actions dropdown** (this replaced three separate buttons per row)
- [ ] Click **Actions** on one of the "still clocked in today" seeded rows
      (Daniel Lee / Nancy Perez / Matthew Thompson / Lisa White, or their
      volunteer counterparts) - a small menu opens with Clock Out / Break
      Start / Break End.
- [ ] Click elsewhere on the page (not the menu) - it closes without doing
      anything.
- [ ] Open it again and press **Escape** - same, closes without acting.
- [ ] Open it again and click **Break Start** - menu closes, page reloads,
      "Break Start" time is now filled in for that row.
- [ ] Open **Actions** again on the same row and click **Break End** - break
      end time fills in.
- [ ] Open **Actions** again and click **Clock Out** - clock-out time fills
      in and Working Hours shows a sensible value (roughly matching how long
      ago you clocked them in, since these are today's real wall-clock
      times).

**The crash-fix regression check**
- [ ] Find **Anthony Harris** (Staff Clockings) or **Anthony Jones**
      (Volunteer Clockings) - clocked in 2 days ago, no clock-out.
- [ ] Open **Actions** → **Clock Out**. This should succeed (page reloads
      normally, no error page) and show **Working: 23:59:59** - not a crash.
      This is the exact scenario that used to throw an unhandled
      `SqlDbType.Time overflow` exception.

**Pagination**
- [ ] Same checks as section 4: page size changes, Previous/Next, no stray
      bullets, disabled-state styling at the first/last page. With 45 rows you
      should see 3 pages at the default page size of 20.

**Table width**
- [ ] At a normal desktop width, the table (including the Actions column)
      should fit without needing to scroll right - this was the point of
      collapsing the three action buttons into one dropdown.

## 6. Hours Worked (individual)

Reach this via **Hours** on any row in Manage Staff/Volunteers.

- [ ] No search box above "Clocking History" - it was removed (this page is
      already scoped to one person, so filtering within it added nothing).
- [ ] Change the Start/End date filters and click **Apply** - table updates to
      match. Click **Clear** - goes back to the default (unfiltered) view.
- [ ] Click **Download PDF** - a PDF downloads with this person's name, date
      range, and hours confirmation text.
- [ ] Scroll to the **Hours Summary** card at the bottom: it should be a
      compact box sized to fit its own content (not stretched across the full
      page width), with the hour figures sitting close to their labels, not
      off in empty space to the far right.

## 7. Reports (sidebar → Reports)

- [ ] **Staff List** / **Volunteer List**: table of all 25 people, pagination
      works, **Download PDF** produces a PDF list.
- [ ] **Staff Clocking** / **Volunteer Clocking** (the report pages, not the
      manual-clocking pages from section 5): pick a specific person from the
      "Select Staff/Volunteer" search box, set a date range covering the last
      30 days, click **Filter** - only that person's rows in that range show
      up. Click **Download PDF**.
- [ ] Leave the person field blank and filter by date range only - should show
      everyone's clockings in that range.

## 8. Kiosk pages (sign-up + clock-in)

These use the simpler, larger-touch-target styling and live outside the main
admin sidebar shell.

**Sign-up** (`/StaffAttendance`, `/VolunteerAttendance`)
- [ ] Fill out "New Staff"/"New Volunteer" with a **brand-new** email/phone
      (not one already in the seed data) and submit - redirects to that
      person's kiosk clock-in screen with a success message.
- [ ] Try again with the **same first/last name** as an existing seeded person
      (e.g. "James Smith") but a different email/phone - should show an amber
      "already registered, is this you?" prompt with **Yes, that's me** /
      **No, this is a different person** buttons, instead of silently creating
      a duplicate.
- [ ] Click **No, this is a different person** - registration proceeds
      normally.
- [ ] Try again with an email/phone that's an **exact match** to an existing
      seeded record - should be blocked outright with a message, no
      confirmation prompt (email/phone matches are treated as certainly the
      same person, unlike a name-only match).
- [ ] In "Existing Staff"/"Existing Volunteer", type part of a seeded name -
      a dropdown of matches appears; click one - redirects straight to that
      person's clock-in screen.
- [ ] Volunteer sign-up only: pick **Mandated Community Hours** from "Which
      volunteer category do you fall under?" - a "Which of these applies to
      you?" dropdown appears. Switch to **Educational Purposes** - that
      dropdown disappears and "Name of Institution"/"Contact Person" fields
      appear instead.

**Clock-in screen** (`/staff-clockin/{id}`, `/volunteer-clockin/{id}`)
- [ ] Four large, clearly-colored buttons (green Clock In, blue Leave for
      Break, amber Return from Break, red Clock Out), no underlines under any
      of them.
- [ ] Click **Clock In** for a newly-registered person - a toast notification
      appears confirming it, then redirects (to Logout or back to the
      attendance page, depending on the Logout Settings toggle - see below).
- [ ] Try clicking **Clock In** again for someone already clocked in today -
      should show a "you have already clocked in today" toast, not a second
      clock-in.

## 9. Logout Settings (sidebar)

- [ ] Toggle **Logout After Every Clocking** off, set a Duration, Save.
- [ ] Go clock someone in via the kiosk flow (section 8) - since the toggle is
      off, it should return you to the attendance page instead of logging you
      out.
- [ ] Toggle it back on, Save, and repeat - this time it should log you out
      after the clock-in action.

## 10. Identity / Manage Account

Reach via **Hello admin@inprem.org** (top-right) → your account, or
`/Identity/Account/Manage`.

- [ ] **Profile**: shows your username, phone number field is editable.
- [ ] **Email**: shows your current email with a confirmed checkmark.
- [ ] **Password**: change-password form renders correctly (don't need to
      actually change it unless you want to).
- [ ] **Two-factor authentication**: page loads, "Add authenticator app" link
      works and shows the QR/key setup instructions as a numbered list (1, 2,
      3 - not bullet points). You don't need to actually complete 2FA setup.
- [ ] **Personal data**: Download button produces a file; don't click Delete
      unless you actually want to delete the admin account.
- [ ] Clicking between these sidebar items keeps the active one highlighted
      correctly.

## 11. Cross-cutting checks (do these anywhere, spot-check a few pages)

- [ ] No text has a stray underline unless it's an actual inline hyperlink
      (sidebar items, buttons, pagination, breadcrumbs should all be
      underline-free).
- [ ] No input/button/card visually overlaps its neighbor at normal desktop
      width.
- [ ] Resize the browser down to a narrow/mobile width (~400px): the sidebar
      hides behind a hamburger icon top-left; tapping it slides the sidebar in
      with a dimming backdrop; tapping the backdrop closes it again.
- [ ] Open browser dev tools console on a few pages and confirm no JavaScript
      errors from the app itself (errors from browser extensions you have
      installed - ad blockers, Grammarly, etc. - are not this app's problem,
      just check the source file in the error).

---

## Resetting between test passes

```powershell
sqlcmd -S localhost -d DB_A65635_inpremdb -E -i scripts\reset-test-data.sql
sqlcmd -S localhost -d DB_A65635_inpremdb -E -i scripts\seed-test-data.sql
```

If the app is running while you do this, nothing bad happens, but pages you
already had open will show stale data until you refresh them.
