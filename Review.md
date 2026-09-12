**What App**
This is an ASP.NET Core 8 Razor Pages timekeeping app for Inprem Holistic Community Resource Center. It manages staff and volunteers, lets them clock in/out and take breaks, tracks attendance, and generates clocking/person reports.

**Findings**
- High: CSRF protection is globally disabled in [Program.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Program.cs:35). Since the app has create/update/move/clocking actions, this makes authenticated users vulnerable to forged POSTs. Re-enable antiforgery globally and add tokens to forms/AJAX calls.

- High: SMTP credentials and an old SQL password are committed in source: [EmailService.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Services/EmailService.cs:10), [Program.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Program.cs:13). Rotate those secrets and move credentials to user secrets, environment variables, or a secret store.

- High: Several API controllers have no `[Authorize]`, including staff lookup, reports, clock controls, and uploads: [ControlsController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ControlsController.cs:9), [ReportsController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ReportsController.cs:9), [SearchController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/SearchController.cs:7), [PeopleController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/PeopleController.cs:7). Even though Razor pages use `[Authorize]`, these endpoints appear directly callable.

- High: Clock-in/out and break actions are implemented as GET endpoints that mutate data, for example [ControlsController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ControlsController.cs:20) and [ControlsController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ControlsController.cs:70). These should be POST/PATCH with antiforgery or API authorization.

- High: Report upload endpoints write user-supplied filenames under `wwwroot\Cache` and return the server file path: [ReportingApiController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ReportingApiController.cs:44), [ReportingApiController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ReportingApiController.cs:63). Validate extensions, generate safe filenames, store outside `wwwroot`, size-limit uploads, and avoid returning physical paths.

- Medium: Volunteer duplicate-email checks query staff instead of volunteers. [VolunteerService.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Services/VolunteerService.cs:26) returns `Staff`, and [Volunteer.cshtml.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Pages/Volunteer.cshtml.cs:49) uses it during volunteer creation. This can allow duplicate volunteers while incorrectly blocking emails that exist as staff.

- Medium: Volunteer “move to staff” sets `Type = "Volunteer"` on the new staff record: [Volunteer.cshtml.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Pages/Volunteer.cshtml.cs:148). That should probably be `"Staff"`.

- Medium: [Volunteer.cshtml](D:/Projects-2/inprem-clocking/InpremClockingApp/Pages/Volunteer.cshtml:180) has a nested `<script>` before closing the first script at [Volunteer.cshtml](D:/Projects-2/inprem-clocking/InpremClockingApp/Pages/Volunteer.cshtml:272). Browser behavior here will be brittle, and some volunteer page JS may not execute correctly.

- Medium: Staff/volunteer pages still reference removed Syncfusion grid elements like `document.getElementById("Grid").ej2_instances[0]`: [Staff.cshtml](D:/Projects-2/inprem-clocking/InpremClockingApp/Pages/Staff.cshtml:269), [Volunteer.cshtml](D:/Projects-2/inprem-clocking/InpremClockingApp/Pages/Volunteer.cshtml:363). Since the current markup is a plain HTML table, these can throw JS errors.

- Medium: Report total calculations sum only the `Hours` and `Minutes` components of `TimeSpan`, which loses whole days and can misstate long totals: [ReportsController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ReportsController.cs:145), [BackOffice.cshtml.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Pages/BackOffice.cshtml.cs:52). Use `WorkingHours.Value.TotalMinutes`.

- Medium: Many queries dereference nullable `CreatedAt` with `CreatedAt!.Value.Date`, for example [ReportsController.cs](D:/Projects-2/inprem-clocking/InpremClockingApp/Controllers/ReportsController.cs:27). Any legacy/null row can crash reports.

**Improvement Areas**
- Consolidate duplicated staff/volunteer clocking logic into one shared service or generic workflow.
- Add role-based authorization: admin for reports/settings/users, limited access for clocking.
- Add database constraints for unique staff/volunteer email and one clocking row per person per date.
- Replace raw `DateTime.Now` with a centralized clock/time-zone policy.
- Clean up stale Syncfusion/BoldReports code and `wwwroot\Cache` artifacts.
- Add tests around clock-in/out, break calculation, duplicate prevention, moving staff/volunteers, and report totals.

Verification: `dotnet build InpremClockingApp.sln /nodeReuse:false` succeeds, with 16 warnings, mostly nullable warnings plus ASP.NET analyzer suggestions. There are no test projects in the repo.