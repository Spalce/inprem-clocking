# Kiosk Review: Reference App vs. Our Implementation

**Date:** 2026-10-03
**Reference reviewed:** https://inpremclockin.org/Main/Index (and its Staff equivalent) — live production, read-only walkthrough
**Compared against:** `InpremClockingApp`'s `VolunteerAttendance`/`StaffAttendance`/`VolunteerClockPage`/`StaffClockPage` pages on `feature/multi-tenancy`

No sign-up form was submitted and no clock-in/break/clock-out button was clicked during this review, since any of those would have created or mutated real production data. Everything below comes from viewing the sign-up screen, opening the "existing person" search, and viewing (without using) the resulting clock-action screen for one real volunteer.

## Structure and access model

The reference app has two separate entry points — `/Main/Index` for volunteers and `/Staff/Index` for staff — each a plain MVC controller/view rather than a shared Razor Pages layout. Functionally these map directly to our `VolunteerAttendance`/`StaffAttendance` pages.

**Login route and access requirements — directly confirmed, not speculative.** With the client's permission, the admin session was logged out and the kiosk/backoffice URLs were re-tested from a genuinely unauthenticated state (view-only; no clock actions were taken), then the admin logged back in afterward. Results, each confirmed with a fresh, cache-busted request:

| Route | Requires login? |
|---|---|
| `/` (public landing page) | No — shows only an "ADMIN" heading with a "Login" button and a decorative "Volunteer Clock-in" banner image that isn't actually a link |
| `/backoffice` | Redirects to Login (as expected) |
| `/Main/Index` (Volunteer sign-up/search) | **Yes** — redirects to `Login?ReturnUrl=%2FMain%2FIndex` |
| `/Staff/Index` (Staff sign-up/search) | **No** — loads directly, HTTP 200, no redirect |
| `/Volunteer/Clocking?VolunteerId={id}` (the actual clock-button screen for an existing volunteer) | **No** — loads directly, full ClockIn/Break/ClockOut screen rendered |

Two things worth calling out precisely:

1. **The reference app has exactly one login page, and it's permanently branded "Admin Log in only"** — confirmed by visiting `/Identity/Account/Login` directly, both bare and with `?ReturnUrl=%2FMain%2FIndex` attached; the heading and form are byte-for-byte identical either way. There is no kiosk-styled variant at all. This is the direct counterpart to the Logout bug we fixed for our own app this session (where the shared login page rendered the wrong style depending on where you came from) — the reference app never solved that problem at all, it just always shows the "Admin" branded screen to anyone who gets redirected there, kiosk user or not.
2. **The reference app is internally inconsistent about which kiosk routes require auth.** The Volunteer *entry* page (`/Main/Index`) is gated, but the Staff entry page and the Volunteer clock-action screen (reachable once you have a `VolunteerId`) are not. This isn't a deliberate "kiosk stays open all day" design — it reads like an `[Authorize]` attribute that's present on one controller/action and missing on others. Not something to replicate; flagged only so it's clear this asymmetry is a reference-app inconsistency, not an intentional pattern worth matching.

Given this, our own blanket `[Authorize]` on every kiosk route (`StaffAttendance`, `VolunteerAttendance`, `staff-clockin/{id}`, `volunteer-clockin/{id}`) is actually **more consistent and arguably more correct** than what the reference does — the open question is purely a product one: does the client want the kiosk device to keep working if the admin's session lapses mid-day, the way the reference app's Staff flow (accidentally or not) allows? Worth a direct product conversation rather than a code fix, since "match the reference" and "do the secure thing" point in different directions here.

## New Volunteer / New Staff sign-up form

Fields, in order: First Name, Last Name, Email Address / Zip Code, Gender (dropdown: blank/M/F), Phone Number / [Sign up]. Identical field set for both Volunteer and Staff.

**No volunteer-category questionnaire exists here at all** — no category dropdown, no Mandate Type, Institution Name, Place of Work, Contact Person, or the Educational-Purposes contact details we just added. This matches what the backoffice review found: the entire questionnaire is something that was added to our implementation specifically, not a feature carried over from (or missing relative to) the reference. Good to have confirmed directly on the kiosk side too, since that's where the questionnaire actually lives for us.

No visual cues (asterisks, inline hints) indicate which fields are required — I did not submit the form to check server-side validation behavior, so I can't compare error-message wording or required-field enforcement one-for-one. Not worth guessing at without submitting real data.

## Existing Volunteer / Existing Staff search

This is the one area where the reference app is internally inconsistent between the two person-types:

- **Volunteer search** is a Select2-style async dropdown ("Search for a volunteer") that live-searches as you type and shows matching **email addresses only** (not names) in the results list, then requires clicking a separate "Search" button to actually navigate to that person's clock screen.
- **Staff search** is a plain text input with a Search button — no live suggestions at all. Submitting it currently throws an unhandled server error (HTTP 500) at `/Staff/SearchStaff` in production. I reproduced this once, read-only (a failed GET/search request changes nothing), to confirm it's a real, currently-broken feature rather than a one-off fluke.

**We're already ahead here, not behind:** our own kiosk search (`/api/search/*`, used identically by both `StaffAttendance` and `VolunteerAttendance`) is a single, working, consistent live-autocomplete implementation for both person types, showing name *and* email in the suggestion list. The reference's Staff search path being broken in production is worth knowing about only as context — it is not something to replicate, and confirms our implementation doesn't need to "catch up" to anything here.

## Clock-action screen

Reached via `/Volunteer/Clocking?VolunteerId={id}` (and presumably a Staff equivalent at a matching URL, not independently verified since reaching it required the currently-broken Staff search). Layout:

- Heading "ClockIn System", "Please clock in", current date, current time.
- Four buttons, always visible regardless of the person's actual current clock state, in this exact order and color: **ClockIn** (green), **Leave for Break** (blue), **Return from Break** (amber/orange), **Clock Out** (red).

This is a strong, confirmed match to our own kiosk clock screens — same wording ("Please clock in"), same four actions in the same order, and the same color-per-action convention (emerald/blue/amber/red) that we specifically restored on the Manage Staff/Volunteer Clocking pages' Actions column earlier this session. No gap here; this confirms that work was correctly aligned with the reference's established visual language.

One small difference: the reference's clock-action screen has no "Home" link back to the sign-up/search screen — a user has to use the browser's Back button. Ours has an explicit Home breadcrumb link. This is arguably an improvement on our part, not a gap.

I did not click any of the four action buttons, so I can't compare the post-click toast/confirmation behavior, redirect timing, or error handling (e.g., "already clocked in today") between the two apps. That would require either a disposable test account or accepting a real mutation against production data, neither of which fits this review's constraints.

## Summary table

| Area | Reference behavior | Our behavior | Note |
|---|---|---|---|
| Login page style | One page, always "Admin Log in only," never varies by ReturnUrl | Two styles (kiosk vs. Back Office), fixed this session | We're ahead — reference never solved this |
| Volunteer entry page (`/Main/Index`) requires sign-in | **Yes** (confirmed) | Yes (`[Authorize]`) | Match |
| Staff entry page (`/Staff/Index`) requires sign-in | **No** (confirmed) | Yes (`[Authorize]`) | Inconsistency in reference, not worth copying — flagged as a product question above |
| Volunteer clock-action screen requires sign-in | **No** (confirmed) | Yes (`[Authorize]`) | Same as above |
| Volunteer-category questionnaire | Not present | Present | We're ahead; confirmed not copied from reference |
| Staff search | Broken in production (500 error), plain text input | Working live autocomplete | We're ahead |
| Volunteer search | Working async dropdown, email-only results | Working live autocomplete, name+email | Roughly equivalent, ours is slightly richer |
| Clock-action screen wording/colors/order | "Please clock in" + 4 buttons, emerald/blue/amber/red | Same | Confirmed match |
| Home/back navigation on clock screen | None (browser Back only) | Explicit Home link | We're ahead |
