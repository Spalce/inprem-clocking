# Behaviour baseline — findings

Found while recording the approved app's behaviour (G0 Step 2, commit `1061ea8`). Each finding has a baseline test tagged `[Trait("Finding", "F-nn")]` that **passes on today's code**. In other words, the test records the current behaviour, even where it looks wrong. G0 fixes nothing.

A fix is a deliberate behaviour change. Make it in a later step, and update the test in the same commit with a reason. Never update a test silently.

Run only these tests: `dotnet test tests/InpremClockingApp.Tests.Baseline --filter "Finding~F-"`

| ID | Severity | Where | What happens today | Test |
|---|---|---|---|---|
| **F-08** | **Fixed in Phase 0 step 0.5** (was: high, tenant isolation) | `Pages/Staff.cshtml.cs` and `Pages/Volunteer.cshtml.cs`, `OnPostMoveAsync` | **Was:** one organization's admin could delete another organization's staff member or volunteer, with all their sessions, by posting the other person's id to the Move handlers. **Now:** `TenantSaveChangesInterceptor` reads each changed or deleted row's real organization from the database and refuses a row of another organization. The request fails (500) and the other organization's rows are untouched. The handler still creates its copy in the caller's own organization first, which is its own unchanged behaviour. | `IsolationWriteTests.Isolation_Move_Page_Handler_With_Beta_Id` (updated in step 0.5; was recording the delete) |
| **F-01** | High: data integrity | `Pages/Staff.cshtml.cs` `OnPostMoveAsync` (the Manage Staff **Move** button) | Moving a staff member with more than one session **fails halfway** with a 500 error. The volunteer and the first session have already been moved, but the staff row and the remaining sessions stay. Moved sessions lose their date (`ClockDate` = 0001-01-01), so the second copy breaks the one-session-per-day index. Commit `73ffd06` fixed this in `api/staff/move`, but no page calls that endpoint. The button calls the page handler, which still has the old code. (`api/staff/move` itself moves every session with its date, but keeps the staff row.) | `PeopleCrudTests.Staff_Move_With_Several_Sessions_Fails_Halfway`, `Staff_Move_Api_Moves_All_Sessions_With_Their_Dates` |
| **F-02** | Medium | `Pages/Volunteer.cshtml.cs` `OnPostMoveAsync` | Moving a volunteer to staff creates a staff row with `Type = "Volunteer"`. Moved sessions lose their date, as in F-01. The code has the same shape as F-01, so a volunteer with several sessions should fail halfway in the same way; that case is not tested separately. | `PeopleCrudTests.Volunteer_Move_Creates_Staff_Typed_As_Volunteer` |
| **F-03** | Medium: wrong totals on a document | Hours Worked page, **Download PDF** button | For the range 6–8 March the page shows 11:00 but the PDF shows 07:30. The button sends `end` as midnight at the start of the end date, and `staff-hours-pdf` / `volunteer-hours-pdf` use it as given, so the end date is left out. The JSON `staff-hours` API and the page itself include the end date. | `PdfAndExportTests.Pdf_From_HoursWorked_Button_Leaves_Out_The_End_Date` |
| **F-04** | Medium | BackOffice dashboard "Calculate Total Hours", and **Download Report** | The "To" date is not included: `RangeEnd` arrives as midnight at the start of that day. **Download Report** uses the same range code (`ResolveRange`); only the dashboard case is tested. | `ReportTotalsTests.Dashboard_Range_End_Date_Is_Not_Included` |
| **F-05** | Medium: wrong branding for other organizations | Every PDF footer (`StaffReportsController`, `VolunteerReportsController`, `BackOffice`, `RosterExport`) | The footer reads "Inprem Admin" for every organization. The header already uses the organization's own name. | `PdfAndExportTests.Pdf_Footer_Says_Inprem_Admin_For_Every_Organization` |
| **F-06** | Low | `Pages/Volunteer.cshtml.cs` `OnPostDeleteAsync` | Volunteer delete does nothing, because the delete code is commented out. The handler redirects and the volunteer stays. Staff have no delete at all. | `PeopleCrudTests.Volunteer_Delete_Does_Nothing` |
| **F-07** | Medium | Staff Clocking / Volunteer Clocking manual entry | A session typed in for a past day is stored with today's `ClockDate`. Two side effects: it blocks that person's kiosk clock-in today, and date filters based on `ClockDate` see it on the wrong day. | `ClockingCrudTests.Clocking_Manual_Entry_For_A_Past_Day_Is_Filed_Under_Today` |
| **F-09** | Low: not reachable from the UI | `Controllers/PeopleController.cs` `GetEmails` | `api/people/emails` without `type` returns 500. From reading the code, the cause is that it runs the staff and volunteer queries at the same time on one `DbContext`. Every current caller passes `type`. | `SearchTests.BackOffice_Email_Suggestions_Without_Type_Fail` |

## Tenant isolation today (summary)

- **Reads are isolated.** Alpha's admin never sees Beta's people, emails or sessions. This was checked on every page, list, export, PDF, search and report API, including requests that use Beta's ids: details pages redirect, Hours Worked returns 404, the hours API returns zero, and search returns 204.
- **Writes are isolated (F-08 fixed in Phase 0 step 0.5).** These attempts on Beta's ids change nothing in Beta's rows (fingerprinted before and after): kiosk buttons, staff and volunteer Update, clocking Edit / Break / ClockOut, manual clocking entry, and `api/staff/move`.
- **Emails and admin accounts are unique per organization only.** Kiosk registration may reuse another organization's email. Two organizations may each have an admin with the same email; the login page tells them apart by password.

## Behaviour the design changes on purpose

These tests are tagged `[Trait("ChangedBy", "...")]`. Update them in the step named, not before.

| Test | Today | Changed by |
|---|---|---|
| `AdminAccountTests.Admin_Same_Email_In_Two_Organizations_Signs_In_By_Password` | Same admin email allowed in two organizations; login picks the account by password | D2 / C3-6, Phase 0 step 0.4 |
| `AccessRuleTests.Login_Failures_Do_Not_Lock_The_Account` | No lockout after failed logins | C3-7, Phase 0 step 0.4 |

## Smaller observations (recorded in tests, not findings)

- An empty `q` on `api/people/emails` and `api/people/staff|volunteers` returns 400 from model binding, so the handler's own empty-query check never runs.
- Kiosk API messages say "staff" on the volunteer endpoints too ("No staff clocking found for today"), and one success message has a typo: "returned fom break".
- `api/staff/save`, `api/staff/all-staff`, `api/staff/all-Volunteer` and the `api/reports/*` POST endpoints are not called by any page. They are covered by the authorization and isolation tests only.
