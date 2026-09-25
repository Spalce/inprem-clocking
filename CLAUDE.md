# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

InpremClockingApp is an ASP.NET Core 8 (Razor Pages + Web API) time-clocking system for **Inprem Holistic Community Resource Center**. It lets staff and volunteers clock in/out and take breaks (via kiosk-style pages hit from a device/QR code), and gives back-office admins a dashboard to manage people, review attendance, and export PDF hour reports.

## Commands

All commands are run from the repo root or `InpremClockingApp/` (the single project referenced by `InpremClockingApp.sln`).

```bash
# restore + build
dotnet build InpremClockingApp.sln

# run locally (reads InpremClockingApp/appsettings.Development.json)
dotnet run --project InpremClockingApp

# EF Core migrations (run from InpremClockingApp/ so the DbContext is discovered)
dotnet ef migrations add <Name>
dotnet ef database update
```

There is **no test project** in the solution and **no CI workflow** under `.github/` yet — don't assume `dotnet test` does anything.

## Architecture

### Data layer
- `Data/ApplicationDbContext.cs` — a single `IdentityDbContext<AppUser>` exposing `Staffs`, `Volunteers`, `ClockingsStaff`, `Clockings`, and `Setting`. ASP.NET Core Identity tables (users/roles) live alongside the app's own tables in the same DB/context.
- `Migrations/` currently has just one migration (`InitialCreate`); the model has moved on since then in places (double-check before assuming the DB schema matches the current model classes).
- Connection string lives in `appsettings.json` (`ConnectionStrings:DefaultConnection`), overridable per environment via `appsettings.Development.json` / `appsettings.Production.json`. `Program.cs` has several **commented-out connection strings, one containing a plaintext password** — leave them alone but never add new live credentials to source; use `dotnet user-secrets` or environment-specific config instead.

### The staff/volunteer duplication pattern
Staff and Volunteers are treated as two parallel but independent domains — there is no shared base type. This duplication shows up at every layer, so a fix or feature for one side usually needs to be mirrored on the other:
- Models: `Models/Staff.cs` + `Models/ClockingStaff.cs` vs `Models/Volunteer.cs` + `Models/Clocking.cs` (same shape: `ClockInTime`, `ClockOutTime`, `LeaveOnBreakTime`, `ReturnOnBreakTime`, `WorkingHours`, `CreatedAt`).
- Services: `StaffService`/`StaffClockingService` vs `VolunteerService`/`VolunteerClockingService` — same method shapes (`SearchByName`, `GetPaged`, `GetClockingReportForStaff`/report-by-date-range, `ClockOut`, `BreakStart`, `BreakEnd`, `Create`/`CreateBatch`/`DeleteBatch`).
- Controllers: `Controllers/Api/StaffReportsController` vs `Controllers/Api/VolunteerReportsController` (hours summary JSON + QuestPDF-generated PDF download); `ControlsController` implements clock-in/out/break endpoints for **both** domains in one file (`staff-clockin/*` and `volunteer-clockin/*` actions side by side).
- Pages: `StaffClockPage`/`StaffAttendance`/`StaffClocking`/`StaffReport`/`OneStaffClockingReport` mirror `VolunteerClockPage`/`VolunteerAttendance`/`VolunteerClocking`/`VolunteerReport`/`OneVolunteerClockingReport`.

Working hours are computed the same way everywhere: `(ClockOutTime - ClockInTime) - (ReturnOnBreakTime - LeaveOnBreakTime)`.

### Controllers
- `Controllers/ControlsController.cs` (`api/controls/...`) — the actual clock-in/out/break-start/break-end/logout-timer endpoints, hit by JS on the kiosk clock pages. Each staff/volunteer can have at most one open clocking record per calendar day (`CreatedAt.Date == DateTime.Now.Date`).
- `Controllers/Api/PeopleController.cs` (`api/people/staff`, `api/people/volunteers`) and `Controllers/PeopleController.cs` (`api/people/emails`) are **two different classes that both route to `api/people`** — they don't collide only because their action route templates differ. Be careful adding new actions to either; check the other file too so you don't introduce a duplicate route.
- `Controllers/Api/StaffReportsController.cs` / `VolunteerReportsController.cs` — hours-worked summaries and QuestPDF PDF generation (`QuestPDF.Settings.License = Community` is set once in `Program.cs`).
- `Controllers/ReportsController.cs`, `Controllers/SearchController.cs`, `Controllers/StaffController.cs` — supporting MVC-style endpoints for the back-office UI.

### Pages (Razor Pages) and layouts
- `Pages/_ViewStart.cshtml` defaults every page to `Layout = "_Layout"` (a plain/kiosk-style layout).
- Back-office/admin pages override this with `Layout = "Shared/_MainLayout"`, an AdminLTE-based shell (`_MainTopmenu` + `_MainSidebar` partials). Kiosk clock pages (`StaffClockPage` at `/staff-clockin/{id:long}`, `VolunteerClockPage` at `/volunteer-clockin/{id:long}`) also use `_MainLayout` despite being meant for a walk-up device — note both are marked `[Authorize]`, so anyone hitting them must already be signed in.
- Almost every page has `@attribute [Authorize]`; auth is ASP.NET Core Identity (`AddDefaultIdentity<AppUser>`), with the full scaffolded Identity UI under `Areas/Identity/Pages/Account/...`. `AppUser`/`AppRole` (`Models/Identity/`) extend `IdentityUser`/`IdentityRole` with a few extra fields (`FirstName`, `LastName`, `Type`, `Description`).
- `BackOffice.cshtml`/`.cshtml.cs` is the admin dashboard landing page — it aggregates counts (staff/volunteer/admin) and total worked hours directly in `OnGet` by loading full tables into memory and summing in C#, not via SQL aggregation.

### PDF reports
QuestPDF (`Document.Create(...).GeneratePdf()`) is used inline inside the report controllers/pages to build downloadable hour-confirmation PDFs (see `StaffReportsController.GetStaffHoursPdf` for the pattern: header with org name, summary box with clocked/break/actual hours, footer). `OneStaffClockingReport`/`OneVolunteerClockingReport` and `StaffClockingReport`/`VolunteerClockingReport` pages render similar reports server-side.

### Front end
Server-rendered Razor + jQuery + AdminLTE (bundled under `wwwroot/lib` and `wwwroot/dist`), plus a single custom `wwwroot/js/site.js`. There's no separate SPA/build step (no `package.json`) — front-end assets are static/vendored, not compiled.
