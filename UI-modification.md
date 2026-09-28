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
  class with `tw:` (`@import "tailwindcss/theme" prefix(tw);` /
  `@import "tailwindcss/utilities" prefix(tw);` — see Phase 1 notes below for why these aren't
  wrapped in `layer(...)` despite Tailwind's own docs suggesting it), so no Tailwind class can
  ever share a name with a Bootstrap/AdminLTE class. This prefix is kept permanently (including
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

## Phase 1 status: done

Converted the shared chrome to Tailwind: `_Layout.cshtml`, `_MainLayout.cshtml` (page shells),
`_Sidebar.cshtml`, `_MainSidebar.cshtml`, `_Topmenu.cshtml`, `_MainTopmenu.cshtml`,
`_LoginPartial.cshtml`, and `_Footer.cshtml`. New look: a dark slate sidebar (was AdminLTE's
navy gradient), a clean white top bar, and the system font stack instead of the "Source Sans
Pro" webfont (dropped — no functional dependency on it, unlike Material Icons, which is kept
because `showToast()` — used by the kiosk clock-in pages — renders its icons with it).

**Two real bugs found and fixed while building this, both worth knowing about for later
phases:**

1. **Automatic content detection doesn't reach `Pages/`.** Tailwind v4's zero-config content
   scanning only walks *downward* from the CSS input file's own directory
   (`wwwroot/css/`) — it never saw `Pages/**/*.cshtml` at all, since that's outside `wwwroot`.
   The very first Phase 0 compile appearing to "find" Bootstrap-colliding classes like
   `.collapse` was a red herring: those came from vendored library source under `wwwroot`, not
   from any page. Fixed with an explicit `@source "../../Pages/**/*.cshtml";` in
   `tailwind-input.css`. **Any new page added in later phases needs no extra config** — this
   one `@source` line covers the whole `Pages/` tree — but this is worth remembering if a
   future stylesheet ever moves or a new content root gets added.
2. **CSS Cascade Layers made every Tailwind utility lose to Bootstrap, silently.** Tailwind's
   recommended granular-import syntax wraps output in `@layer theme`/`@layer utilities`. Per
   the CSS spec, *any* layered style loses to *any* unlayered style, regardless of specificity
   or source order — and Bootstrap/AdminLTE's CSS is unlayered. The result: `.tw\:text-slate-500`
   was silently losing to Bootstrap's plain `a { color: #007bff }`, even though a class
   selector should always beat an element selector. Fixed by dropping the `layer(...)`
   qualifier from both imports in `tailwind-input.css`, so Tailwind's rules are plain unlayered
   CSS and compete on normal specificity/order rules (where they correctly win). Net effect:
   **don't reintroduce `layer(theme)`/`layer(utilities)` while Bootstrap/AdminLTE is still
   loaded** — safe to revisit once they're removed in Phase 4, though there's no real benefit
   to changing it back even then.

Also worth noting for future phases: **a running `dotnet run` does not pick up `.cshtml`
changes automatically** (views are compiled into the assembly at build time, not hot-reloaded)
— every markup change needs a rebuild + restart before it's visible, or it'll look like the
change "didn't work."

Replaced two AdminLTE JS widget interactions with a small custom script
(`wwwroot/js/layout.js`, ~35 lines) instead of carrying AdminLTE's JS forward for chrome that
no longer uses AdminLTE's classes: the mobile sidebar drawer (was `data-widget="pushmenu"`)
and the "Reports" collapsible submenu (was `data-widget="treeview"`). Bootstrap/AdminLTE's own
JS files are still loaded, untouched, for page-specific content not yet converted.

**Verified live:** dashboard and sidebar navigation, the "Reports" submenu expand/collapse
(the exact interaction the Cascade Layers/collision risk could have broken), the mobile
hamburger drawer + backdrop-click-to-close at a 480px viewport, and a full functional
smoke test on the kiosk side — registered a real volunteer through `VolunteerAttendance`
(including hitting its own field validation correctly), landed on the clock-in page, and
clocked in successfully. Test data cleaned up from the dev DB afterward. No regressions found.

**Local dev commands** (from `InpremClockingApp/`, using the binary in `../tools/`):
- One-time build: `../tools/tailwindcss-windows-x64.exe -i wwwroot/css/tailwind-input.css -o wwwroot/css/tailwind.css --minify`
- Watch while editing: `../tools/tailwindcss-windows-x64.exe -i wwwroot/css/tailwind-input.css -o wwwroot/css/tailwind.css --watch`

## Phase 2 status: done

Converted the core admin CRUD/reporting pages: `Staff.cshtml`, `Volunteer.cshtml`,
`StaffClocking.cshtml`, `VolunteerClocking.cshtml`, `StaffReport.cshtml`,
`VolunteerReport.cshtml`, `User.cshtml` (Admins), `StaffClockingReport.cshtml`,
`VolunteerClockingReport.cshtml`, `OneStaffClockingReport.cshtml`,
`OneVolunteerClockingReport.cshtml`, plus `HoursWorked.cshtml` (see below for why it was added
to this phase's scope). Established a small set of reusable Tailwind class strings per page
(`input`, `label`, `btnPrimary`, `btnWhite`, `btnDanger`, `actionBtn`) applied via Razor
`@{ var x = "..."; }` locals — same visual language as Phase 1's chrome. Bootstrap modal
mechanics (`modal fade`, `data-dismiss`, jQuery `.modal('show'/'hide')`) are kept as-is for the
Edit/Add dialogs on Staff/Volunteer — only their inner content is restyled, per the guardrails.

**Branch had to absorb two other in-flight branches before this phase's markup would compile.**
`ui/tailwind-modernization` was created off `main`, but the Phase 2 markup assumes page-model
properties (pagination, `Search`, PDF handler routes, `StaffId`/`Start`/`End` on the "One..."
reports) that only exist on `fix/pagination-and-hours-display` and
`fix/duplicate-registration-prevention` — neither merged into `main` yet. Stashed the in-progress
Tailwind markup, merged both branches into `ui/tailwind-modernization` (`--no-edit`, both clean,
no conflicts), confirmed the merged base builds with 0 errors, then reapplied the Tailwind
conversions on top. This only changed `ui/tailwind-modernization` locally — `main` and the two
source branches are untouched.

**Found and fixed a real Phase-1 layout bug that affects every page in the app, not just Phase
2's.** The standard page container (`tw:mx-auto tw:max-w-7xl tw:px-4 ...`, used on every
Tailwind-converted page) is a direct child of the shared layout's `tw:flex tw:flex-col` shell.
Per the CSS flexbox spec, a flex item with an auto margin on the cross axis (`mx-auto`, in a
column flex container) has its `stretch` alignment disabled and sizes to its own content's
preferred width instead of the available space — invisible on narrow pages, but on any page
with a wide table (7-8 columns), the container quietly rendered ~150px wider than the viewport,
and because that oversized box was a sibling of the top bar and footer (not a descendant of the
table's own `overflow-x-auto` wrapper), the *entire page* — top bar and footer included —
became horizontally scrollable instead of just the table. Root-caused via `getBoundingClientRect`
on every element to find which one exceeded the viewport, rather than guessing. Fixed with a
single wrapping `<div class="tw:w-full tw:min-w-0">@RenderBody()</div>` in both
`_Layout.cshtml` and `_MainLayout.cshtml` — an explicit `width:100%` on that wrapper (rather
than relying on stretch) sidesteps the auto-margin rule entirely, and `min-w-0` lets it shrink
below its content's intrinsic width so the table's own `overflow-x-auto` div is what scrolls.
Verified via `scrollWidth`/`innerWidth` comparison and visually: the table now scrolls in its own
contained horizontal scrollbar while the sidebar, top bar, and footer stay fixed.

**`HoursWorked.cshtml` was pulled into this phase's scope.** It didn't exist when Phase 2 was
planned — it arrived via the `fix/pagination-and-hours-display` merge above, and the "Hours"
action link on both Staff and Volunteer list pages routes directly to it. Since it's a primary,
one-click destination from pages just converted, leaving it in raw Bootstrap/AdminLTE markup
would have been a jarring inconsistency, so it was converted to the same Tailwind design system
as part of this phase. Its predecessor — an in-page "hours" Bootstrap modal that Staff.cshtml
and Volunteer.cshtml had accumulated from an earlier iteration of this phase, superseded by the
dedicated page and no longer reachable from any visible button — was removed from both files
(dead modal markup + its now-unused `btnCalculate`/`btnDownloadPdf`/`btn-hours` JS listeners).

**Verified live in the browser**, all 12 pages: Staff/Volunteer (search, Add New, Edit modal
pre-filling correctly, pagination, horizontal table scroll contained after the layout fix),
StaffClocking/VolunteerClocking (manual clock-in form, colored Clock Out/Break Start/Break End
action buttons), StaffReport/VolunteerReport (table + working PDF download, confirmed via direct
`fetch()` returning a valid 200 OK PDF byte stream), Admins (create form + table),
StaffClockingReport/VolunteerClockingReport (filter form + results), the two "Individual ..."
report pages reached via the Reports sidebar submenu (submenu expand/collapse still works), and
HoursWorked (reached from both Staff and Volunteer "Hours" links, filter/summary/PDF all intact).
Two console `[EXCEPTION] Object` entries observed during testing were confirmed to originate
from third-party Chrome extensions active in the test browser profile (maxai, Quillbot, and
similar), not from the app — they fire on every page load regardless of app code.

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
