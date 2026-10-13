using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace InpremClockingApp.Tests.Baseline.Helpers;

public static class Sessions
{
    /// <summary>A client signed in (through the login page) as the organization's admin.</summary>
    public static async Task<HttpClient> AdminOfAsync(this AppFixture app, TenantSeed tenant)
    {
        var client = app.CreateClient();
        await SignIn.AsAsync(client, tenant.Admin!.Email, app.Data.Password);
        return client;
    }

    /// <summary>
    /// A client signed in as an account with no role. The app has no UI that creates one today
    /// (CreateAdmin always grants Admin), so the account is created directly.
    /// </summary>
    public static async Task<HttpClient> NoRoleUserOfAsync(this AppFixture app, TenantSeed tenant)
    {
        var email = $"norole-{Guid.NewGuid():N}@{tenant.Name.ToLowerInvariant()}.test";
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var result = await users.CreateAsync(
                new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = tenant.Id },
                app.Data.Password);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var client = app.CreateClient();
        await SignIn.AsAsync(client, email, app.Data.Password);
        return client;
    }
}

/// <summary>Arranges extra rows for tests that change data, so each test has people of its own.</summary>
public static class Arrange
{
    public static async Task<long> AddStaffAsync(this AppFixture app, TenantSeed tenant, string firstName = "Kiosk", string lastName = "Tester")
    {
        await using var db = app.CreateDbContext();
        var unique = Guid.NewGuid().ToString("N")[..10];
        var staff = new Staff
        {
            TenantId = tenant.Id,
            EmailAddress = $"staff-{unique}@{tenant.Name.ToLowerInvariant()}.test",
            FirstName = firstName,
            LastName = lastName,
            ZipCode = "43229",
            Gender = 'F',
            Type = "Staff",
            PhoneNumber = "614" + Random.Shared.NextInt64(1_000_000, 9_999_999),
            Address = "5 Arrange Street, Testville",
            CreatedAt = DateTime.UtcNow,
        };
        db.Staffs.Add(staff);
        await db.SaveChangesAsync();
        return staff.StaffId;
    }

    public static async Task<long> AddVolunteerAsync(this AppFixture app, TenantSeed tenant, string firstName = "Kiosk", string lastName = "Tester")
    {
        await using var db = app.CreateDbContext();
        var unique = Guid.NewGuid().ToString("N")[..10];
        var volunteer = new Volunteer
        {
            TenantId = tenant.Id,
            EmailAddress = $"volunteer-{unique}@{tenant.Name.ToLowerInvariant()}.test",
            FirstName = firstName,
            LastName = lastName,
            ZipCode = "43229",
            Gender = 'M',
            Type = "Volunteer",
            PhoneNumber = "614" + Random.Shared.NextInt64(1_000_000, 9_999_999),
            Address = "6 Arrange Street, Testville",
            VolunteerCategory = VolunteerCategories.PersonalOrIndividualVolunteering,
            CreatedAt = DateTime.UtcNow,
        };
        db.Volunteers.Add(volunteer);
        await db.SaveChangesAsync();
        return volunteer.VolunteerId;
    }

    /// <summary>Today's calendar date in the organization's time zone (what the app uses as ClockDate).</summary>
    public static DateTime TodayIn(TenantSeed tenant) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZoneId)).Date;
}
