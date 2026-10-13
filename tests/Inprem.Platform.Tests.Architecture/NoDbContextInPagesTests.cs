using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inprem.Platform.Tests.Architecture;

/// <summary>
/// Pages and endpoints are thin (design section 3): no page model or controller may take a
/// DbContext, except the approved app's existing ones listed below (C1/C2 files, which may not be
/// restructured). A new page or controller that injects or stores a DbContext fails this test.
/// When a listed one stops using a DbContext (e.g. Login in step 0.4), remove it from the list.
/// </summary>
public class NoDbContextInPagesTests
{
    private static readonly HashSet<string> ApprovedAppTypesUsingDbContext = new()
    {
        // Login, ForgotPassword and ResetPassword left this list in Phase 0 step 0.4 (standard Identity calls).
        "InpremClockingApp.Controllers.ControlsController",
        "InpremClockingApp.Controllers.PeopleController",
        "InpremClockingApp.Controllers.ReportsController",
        "InpremClockingApp.Controllers.SearchController",
        "InpremClockingApp.Controllers.StaffController",
        "InpremClockingApp.Pages.BackOfficeModel",
        "InpremClockingApp.Pages.Staff",
        "InpremClockingApp.Pages.StaffAttendance",
        "InpremClockingApp.Pages.Volunteer",
        "InpremClockingApp.Pages.VolunteerAttendance",
    };

    private static readonly Assembly WebApp = typeof(InpremClockingApp.Data.ApplicationDbContext).Assembly;

    [Fact]
    public void New_Pages_And_Controllers_Do_Not_Use_A_DbContext()
    {
        var offenders = PagesAndControllersUsingDbContext().Where(t => !ApprovedAppTypesUsingDbContext.Contains(t)).ToList();

        Assert.True(offenders.Count == 0,
            "These pages/controllers use a DbContext; put the data access in an Application use case instead: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void Approved_List_Has_No_Stale_Entries()
    {
        var stale = ApprovedAppTypesUsingDbContext.Except(PagesAndControllersUsingDbContext()).ToList();

        Assert.True(stale.Count == 0, "No longer use a DbContext; remove from the approved list: " + string.Join(", ", stale));
    }

    [Fact]
    public void The_Check_Sees_Pages_And_Controllers()
    {
        // Guards against the scan silently finding nothing (e.g. a wrong assembly).
        Assert.Contains("InpremClockingApp.Pages.Staff", PagesAndControllersUsingDbContext());
        Assert.Contains("InpremClockingApp.Controllers.ControlsController", PagesAndControllersUsingDbContext());
    }

    private static List<string> PagesAndControllersUsingDbContext()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        return WebApp.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        (typeof(PageModel).IsAssignableFrom(t) || typeof(ControllerBase).IsAssignableFrom(t)))
            .Where(t =>
                t.GetConstructors(members).SelectMany(c => c.GetParameters()).Any(p => IsDbContext(p.ParameterType)) ||
                t.GetFields(members).Any(f => IsDbContext(f.FieldType)) ||
                t.GetProperties(members).Any(p => IsDbContext(p.PropertyType)))
            .Select(t => t.FullName!)
            .OrderBy(n => n)
            .ToList();
    }

    private static bool IsDbContext(Type type) => typeof(DbContext).IsAssignableFrom(type);
}
