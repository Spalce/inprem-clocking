# UI Modernization Plan (Tailwind CSS)

## Goal

Modernize the visual design of InpremClockingApp using Tailwind CSS, for long-term styling
flexibility. This is a **presentation-layer change only** — no business logic, data flow,
routes, or database structure changes as part of this work.

## Branch

All work happens on `ui/tailwind-modernization`, created off `main`. Nothing here affects
`main` or any other branch (`fix/pagination-and-hours-display`,
`fix/duplicate-registration-prevention`, etc.) until this branch is reviewed and explicitly
merged. Nothing is pushed to `origin` or merged by me — that's reserved for you.

## Guardrails (apply to every phase, no exceptions)

- No changes to any `.cshtml.cs` PageModel, controller, or service logic.
- No new/removed/renamed routes.
- No database or migration changes.
- No changes to element `id`, `name`, or `asp-for` bindings that JavaScript or model binding
  depend on — only `class` attributes and surrounding markup structure change. Anywhere a
  behavior depends on a specific ID (autocomplete widgets, pagination forms, the duplicate-check
  confirmation flow, etc.), that ID is preserved exactly.
- Existing AdminLTE/Bootstrap/jQuery assets stay in place and working until the last page that
  depends on them is converted — the app is never left in a broken in-between state.
- Every phase is verified live in the browser (forms submit, buttons work, pagination works,
  PDF downloads work, clock-in flows work) before moving to the next phase.

## Technical approach

The project currently has no build pipeline (no `package.json`, no npm) — all CSS/JS is
vendored and committed directly under `wwwroot`. To avoid introducing Node.js/npm as a new
project dependency, this uses the **Tailwind standalone CLI** — a single downloadable
executable, no Node install required, on any machine including the deployment target.

- A small input stylesheet (`wwwroot/css/tailwind-input.css`) with Tailwind's directives,
  compiled to one output file (`wwwroot/css/tailwind.css`).
- The **compiled output is committed to source control**, exactly like the currently-vendored
  Bootstrap/AdminLTE CSS. This means `dotnet build`/`dotnet publish` and the existing VS
  Code / Web Deploy / FTP deployment to SmarterASP.NET need **zero changes** — the compiled CSS
  is just another static file. Running the CLI with `--watch` is a dev-time convenience for
  iterating on styles locally; it is never required to build or deploy the app.
- All Tailwind classes are generated with a **`tw:` prefix** (e.g. `tw:flex`, `tw:p-4`), and
  Preflight (Tailwind's base CSS reset) is deliberately left out for now — see the Phase 0
  notes below for why both of those decisions exist.

## Phase 0 status: done

- Downloaded the Tailwind standalone CLI (`v4.3.3`, Windows x64) to `/tools/` at the repo
  root. This binary is **not committed** (large, platform-specific) — `.gitignore` now
  excludes `/tools/`. To re-fetch it:
  ```
  curl -sL -o tools/tailwindcss-windows-x64.exe https://github.com/tailwindlabs/tailwindcss/releases/download/v4.3.3/tailwindcss-windows-x64.exe
  ```
- Created `wwwroot/css/tailwind-input.css`, importing only Tailwind's `theme` and `utilities`
  layers (not `preflight`). Preflight resets default browser styling for headings, lists,
  buttons, etc. — while Bootstrap/AdminLTE is still in place (through Phase 3), Preflight
  would fight with Bootstrap's own reset on every page not yet converted. It gets added back
  once Bootstrap/AdminLTE is fully removed in Phase 4.
- **Found a real collision risk and fixed it structurally, not by luck:** compiling without a
  prefix first, Tailwind generated classes named `.collapse`, `.sr-only`, and `.invisible` —
  because those literal class names already appear in the codebase (from Bootstrap/AdminLTE
  usage), Tailwind's content scanner picked them up. Bootstrap already defines its own
  `.collapse` (used by AdminLTE's collapsible sidebar menus, e.g. "Reports"), `.sr-only`, and
  `.invisible` with different behavior — loading both would have let Tailwind's version win the
  cascade and silently broken those Bootstrap components. Fixed by prefixing every generated
  class with `tw:` (`@import "tailwindcss/theme" layer(theme) prefix(tw);` /
  `@import "tailwindcss/utilities" layer(utilities) prefix(tw);`), so no Tailwind class can ever
  share a name with a Bootstrap/AdminLTE class. This prefix is kept permanently (including
  after Bootstrap is removed in Phase 4) rather than stripped later — there's no real downside
  to keeping it, and removing it would mean re-touching every already-converted page for no
  functional gain.
- Compiled the initial (currently near-empty, since no page uses any `tw:` class yet) output
  to `wwwroot/css/tailwind.css` and referenced it via `<link>` in both `_Layout.cshtml` and
  `_MainLayout.cshtml` (the app actually uses both — `_Layout` is the default per
  `_ViewStart.cshtml` and backs most admin pages via the `_Sidebar`/`_Topmenu` partials;
  `_MainLayout` backs the kiosk sign-up pages via `_MainSidebar`/`_MainTopmenu`).
- Verified live in the browser: the admin dashboard, the sidebar's collapsible "Reports"
  submenu specifically (the exact Bootstrap `.collapse` interaction at risk), and the
  Volunteer sign-up kiosk page all render and behave identically to before. Zero visual or
  functional change, as required.

**Local dev commands** (from `InpremClockingApp/`, using the binary in `../tools/`):
- One-time build: `../tools/tailwindcss-windows-x64.exe -i wwwroot/css/tailwind-input.css -o wwwroot/css/tailwind.css --minify`
- Watch while editing: `../tools/tailwindcss-windows-x64.exe -i wwwroot/css/tailwind-input.css -o wwwroot/css/tailwind.css --watch`

## Phased plan

**Phase 0 — Foundation (no visible change)**
Add the Tailwind CLI, input/output CSS files, and a config scoped to this project's `Pages`
folder. Reference the compiled stylesheet in the shared layout *alongside* the existing
Bootstrap/AdminLTE CSS — no page uses Tailwind classes yet, so there is no visual change.
Verify: app builds and runs exactly as it does today.

**Phase 1 — Shared chrome (sidebar, top bar, page shell)**
Convert `_MainLayout.cshtml` and the sidebar/topmenu partials that every admin page inherits.
Biggest visual impact for the lowest risk, since it's shared structural chrome, not
page-specific forms or business logic.
Verify: every admin page still loads correctly under the new shell; all nav links, login,
and logout still work.

**Phase 2 — Core admin pages (tables, forms, pagination)**
Manage Staff, Manage Volunteers, Staff/Volunteer Report, Admins, the clocking pages
(StaffClocking/VolunteerClocking), and the report pages (StaffClockingReport/
VolunteerClockingReport + the individual "One..." reports).
Verify per page: search/filter, Edit/Move actions, pagination navigation, and PDF download
buttons all still work exactly as before.

**Phase 3 — Kiosk pages (sign-up + clock-in)**
VolunteerAttendance/StaffAttendance sign-up forms (including the duplicate-check
confirmation prompts) and the StaffClockPage/VolunteerClockPage clock-in screens. These get a
deliberately simpler, larger-touch-target treatment suited to a walk-up kiosk device, distinct
from the admin backoffice look.
Verify: registration, the duplicate-check confirm/deny flow, and clock-in/break/clock-out
actions all still work exactly as before.

**Phase 4 — Remaining pages + cleanup**
BackOffice dashboard, Settings, Identity "Manage Account" pages, Login/Register. Once every
page is converted, remove the now-unused AdminLTE/Bootstrap 4/jQuery UI assets (this is also
where the old jQuery UI autocomplete widget on the sign-up pages gets replaced with the
already-established custom dropdown pattern).
Verify: full click-through of the app; nothing references a removed asset.

**Phase 5 — Final review pass**
Side-by-side check against the current app for anything visually broken or functionally
regressed. Hand the branch back for your review — no merge or push without your explicit
go-ahead.

## What I'll handle

- All code changes for every phase above, in isolation on this branch.
- Installing/configuring the Tailwind CLI, compiling CSS, converting markup page by page.
- Functional verification in-browser after each phase.
- Keeping this document updated with progress/status as phases complete.

## What you'll need to handle

- Review checkpoints — at minimum after Phase 1 (shared shell) and Phase 3 (kiosk pages),
  since those are the biggest visual jumps. Tell me to continue, adjust, or stop.
- Branding preferences (color palette, logo), if any. Without input I'll use a clean, neutral
  default palette — easy to change later since Tailwind centralizes colors in one config file.
- The final merge to `main` — I will not do this myself, per your instruction.
- If you want to tweak styles yourself later: running the watch build only requires the single
  standalone CLI binary, no npm/Node install. I'll document the exact one-line command once
  Phase 0 lands.

## Explicitly out of scope

- PDF report generation (QuestPDF) — unrelated to web CSS, untouched.
- Business logic: validation rules, duplicate-registration prevention, pagination logic,
  authorization/roles.
- Database schema/migrations.
- Multi-tenancy (discussed separately, not started).
