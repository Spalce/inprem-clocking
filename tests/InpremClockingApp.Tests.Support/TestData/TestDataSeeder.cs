using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InpremClockingApp.Tests.Support.TestData;

/// <summary>
/// Seeds the standard test organizations into a fresh database:
/// <list type="bullet">
/// <item><b>Alpha</b> (<c>America/New_York</c>) and <b>Beta</b> (<c>Europe/London</c>): an owner, an admin,
/// three staff, three volunteers, and a completed session for every person on four days,
/// one of which is that zone's spring daylight-saving change.</item>
/// <item><b>Gamma</b>: the organization row only.</item>
/// </list>
/// The house organization created by the migrations is looked up, not created.
/// Rows are written with explicit TenantIds, outside any request (no tenant is resolved, so the
/// app's automatic TenantId stamping does not apply).
/// </summary>
public static class TestDataSeeder
{
    // US spring change 2026: Sunday 8 March, 02:00 EST -> 03:00 EDT.
    private static readonly ClockingDay[] AlphaDays =
    {
        NormalDay(new DateOnly(2026, 3, 5)),
        NormalDay(new DateOnly(2026, 3, 6)),
        // 01:00 EST (06:00Z) -> 06:00 EDT (10:00Z) is 4h elapsed, minus a 30 min break.
        new(new DateOnly(2026, 3, 8), new TimeOnly(1, 0), new TimeOnly(3, 0), new TimeOnly(3, 30), new TimeOnly(6, 0),
            TimeSpan.FromHours(3.5), IsDaylightSavingChangeDay: true),
        NormalDay(new DateOnly(2026, 3, 9)),
    };

    // UK spring change 2026: Sunday 29 March, 01:00 GMT -> 02:00 BST.
    private static readonly ClockingDay[] BetaDays =
    {
        NormalDay(new DateOnly(2026, 3, 26)),
        NormalDay(new DateOnly(2026, 3, 27)),
        // 00:30 GMT (00:30Z) -> 05:00 BST (04:00Z) is 3.5h elapsed, minus a 30 min break.
        new(new DateOnly(2026, 3, 29), new TimeOnly(0, 30), new TimeOnly(2, 0), new TimeOnly(2, 30), new TimeOnly(5, 0),
            TimeSpan.FromHours(3), IsDaylightSavingChangeDay: true),
        NormalDay(new DateOnly(2026, 3, 30)),
    };

    // 09:00-17:00 with a 12:00-12:30 break.
    private static ClockingDay NormalDay(DateOnly date) =>
        new(date, new TimeOnly(9, 0), new TimeOnly(12, 0), new TimeOnly(12, 30), new TimeOnly(17, 0),
            TimeSpan.FromHours(7.5), IsDaylightSavingChangeDay: false);

    public static async Task<SeedCatalog> SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var password = $"T{Guid.NewGuid():N}!a";

        var house = await db.Tenants.IgnoreQueryFilters().OrderBy(t => t.Id).FirstAsync();

        var alpha = await SeedTenantAsync(db, users, password, "Alpha", "America/New_York", "alpha",
            staffNames: new[] { ("Avery", "Stone"), ("Blake", "Rivers"), ("Casey", "Fields") },
            volunteerNames: new[] { ("Dana", "Brooks"), ("Emery", "Woods"), ("Finley", "Hart") },
            AlphaDays);

        var beta = await SeedTenantAsync(db, users, password, "Beta", "Europe/London", "beta",
            staffNames: new[] { ("Gale", "Marsh"), ("Harper", "Lane"), ("Indy", "Moss") },
            volunteerNames: new[] { ("Jules", "Ford"), ("Kai", "Banks"), ("Logan", "Reed") },
            BetaDays);

        var gammaTenant = new Tenant { Name = "Gamma", TimeZoneId = "America/Chicago", CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) };
        db.Tenants.Add(gammaTenant);
        await db.SaveChangesAsync();

        return new SeedCatalog
        {
            House = new TenantSeed { Id = house.Id, Name = house.Name, TimeZoneId = house.TimeZoneId },
            Alpha = alpha,
            Beta = beta,
            Gamma = new TenantSeed { Id = gammaTenant.Id, Name = gammaTenant.Name, TimeZoneId = gammaTenant.TimeZoneId },
            Password = password,
        };
    }

    private static async Task<TenantSeed> SeedTenantAsync(
        ApplicationDbContext db,
        UserManager<AppUser> users,
        string password,
        string name,
        string timeZoneId,
        string domain,
        (string First, string Last)[] staffNames,
        (string First, string Last)[] volunteerNames,
        ClockingDay[] days)
    {
        var createdUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var tenant = new Tenant
        {
            Name = name,
            TimeZoneId = timeZoneId,
            CreatedAt = createdUtc,
            Address = $"1 {name} Test Street, Testville",
            ContactInfo = $"555-0100 | office@{domain}.test",
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var owner = await CreateAdminAsync(users, tenant.Id, $"owner@{domain}.test", "Olive", name, password);
        var admin = await CreateAdminAsync(users, tenant.Id, $"admin@{domain}.test", "Adrian", name, password);

        var staff = staffNames.Select((n, i) => new Staff
        {
            TenantId = tenant.Id,
            EmailAddress = $"staff{i + 1}@{domain}.test",
            FirstName = n.First,
            LastName = n.Last,
            ZipCode = "43229",
            Gender = i % 2 == 0 ? 'F' : 'M',
            Type = "Staff",
            PhoneNumber = $"614555010{i + 1}",
            Address = $"{i + 10} Staff Road, Testville",
            CreatedAt = createdUtc,
        }).ToList();
        var volunteers = volunteerNames.Select((n, i) => new Volunteer
        {
            TenantId = tenant.Id,
            EmailAddress = $"volunteer{i + 1}@{domain}.test",
            FirstName = n.First,
            LastName = n.Last,
            ZipCode = "43229",
            Gender = i % 2 == 0 ? 'M' : 'F',
            Type = "Volunteer",
            PhoneNumber = $"614555020{i + 1}",
            Address = $"{i + 20} Volunteer Road, Testville",
            VolunteerCategory = VolunteerCategories.PersonalOrIndividualVolunteering,
            CreatedAt = createdUtc,
        }).ToList();
        db.Staffs.AddRange(staff);
        db.Volunteers.AddRange(volunteers);
        await db.SaveChangesAsync();

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        foreach (var day in days)
        {
            var inUtc = ToUtc(day.Date, day.ClockIn, zone);
            var breakStartUtc = ToUtc(day.Date, day.BreakStart, zone);
            var breakEndUtc = ToUtc(day.Date, day.BreakEnd, zone);
            var outUtc = ToUtc(day.Date, day.ClockOut, zone);
            var clockDate = day.Date.ToDateTime(TimeOnly.MinValue);

            db.ClockingsStaff.AddRange(staff.Select(s => new ClockingStaff
            {
                TenantId = tenant.Id,
                StafId = s.StaffId,
                FullName = s.FullName,
                ClockInTime = inUtc,
                LeaveOnBreakTime = breakStartUtc,
                ReturnOnBreakTime = breakEndUtc,
                ClockOutTime = outUtc,
                WorkingHours = day.ExpectedWorkingHours,
                CreatedAt = inUtc,
                ClockDate = clockDate,
            }));
            db.Clockings.AddRange(volunteers.Select(v => new Clocking
            {
                TenantId = tenant.Id,
                VoluntId = v.VolunteerId,
                FullName = v.FullName,
                ClockInTime = inUtc,
                LeaveOnBreakTime = breakStartUtc,
                ReturnOnBreakTime = breakEndUtc,
                ClockOutTime = outUtc,
                WorkingHours = day.ExpectedWorkingHours,
                CreatedAt = inUtc,
                ClockDate = clockDate,
            }));
        }
        await db.SaveChangesAsync();

        return new TenantSeed
        {
            Id = tenant.Id,
            Name = name,
            TimeZoneId = timeZoneId,
            Owner = owner,
            Admin = admin,
            Staff = staff.Select(s => new PersonSeed(s.StaffId, s.EmailAddress, s.FirstName!, s.LastName!)).ToList(),
            Volunteers = volunteers.Select(v => new PersonSeed(v.VolunteerId, v.EmailAddress, v.FirstName!, v.LastName!)).ToList(),
            ClockingDays = days,
        };
    }

    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), zone);

    // Same shape as AuthService.CreateAdmin: UserName = Email, Type = Admin, Admin role.
    private static async Task<AdminSeed> CreateAdminAsync(
        UserManager<AppUser> users, int tenantId, string email, string firstName, string lastName, string password)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            Type = IdentitySeeder.AdminRole,
            TenantId = tenantId,
        };
        Check(await users.CreateAsync(user, password), email);
        Check(await users.AddToRoleAsync(user, IdentitySeeder.AdminRole), email);
        return new AdminSeed(user.Id, email, firstName, lastName);
    }

    private static void Check(IdentityResult result, string email)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Seeding {email} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
