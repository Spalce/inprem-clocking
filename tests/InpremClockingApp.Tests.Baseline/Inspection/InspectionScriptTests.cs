using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static InpremClockingApp.Tests.Baseline.Inspection.InspectionScript;

namespace InpremClockingApp.Tests.Baseline.Inspection;

/// <summary>
/// scripts/inspection/*.sql against the seeded test database: they run, refuse to run without
/// the local-copy confirmation, and report the seed data correctly.
/// </summary>
[Collection(ReadOnlyAppCollection.Name)]
public class InspectionScriptTests
{
    private readonly AppFixture _app;

    public InspectionScriptTests(AppFixture app) => _app = app;

    [Theory]
    [InlineData("01-environment.sql")]
    [InlineData("02-migration-history.sql")]
    [InlineData("03-row-counts.sql")]
    [InlineData("04-data-quality.sql")]
    [InlineData("05-report-baseline.sql")]
    [InlineData("04p-data-quality-pre-tenancy.sql")]
    [InlineData("05p-report-baseline-pre-tenancy.sql")]
    public async Task Inspection_Script_Refuses_Without_Local_Copy_Confirmation(string script)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => RunAsync(_app.ConnectionString, script, confirmLocalCopy: "NO"));

        Assert.Contains("Refusing to run", error.Message);
    }

    [Fact]
    public async Task Inspection_01_Environment()
    {
        var row = Assert.Single((await RunAsync(_app.ConnectionString, "01-environment.sql"))[0]);

        Assert.Equal(new SqlConnectionStringBuilder(_app.ConnectionString).InitialCatalog, row["DatabaseName"]);
        Assert.Equal(1, row["IsDbOwner"]);
        Assert.NotNull(row["CompatibilityLevel"]);
    }

    [Fact]
    public async Task Inspection_02_Migration_History_Matches_The_Branch()
    {
        var results = await RunAsync(_app.ConnectionString, "02-migration-history.sql");

        // The approved app's 15 migrations plus Phase 0's M3 and M4.
        Assert.Equal(17, results[0].Count);
        Assert.Empty(results[1]);
        Assert.Empty(results[2]);
        var summary = Assert.Single(results[3]);
        Assert.Equal("20261013175341_M4_CompositeTenantKeys", summary["LastApplied"]);
        Assert.Equal(summary["LastExpected"], summary["LastApplied"]);
    }

    [Fact]
    public async Task Inspection_03_Row_Counts()
    {
        var counts = (await RunAsync(_app.ConnectionString, "03-row-counts.sql"))[0]
            .ToDictionary(r => $"{r["SchemaName"]}.{r["TableName"]}", r => Convert.ToInt64(r["RowCount"]));

        Assert.Equal(4, counts["dbo.Tenants"]);
        Assert.Equal(4, counts["dbo.AspNetUsers"]);
        Assert.Equal(6, counts["dbo.Staffs"]);
        Assert.Equal(6, counts["dbo.Volunteers"]);
        Assert.Equal(24, counts["dbo.ClockingsStaff"]);
        Assert.Equal(24, counts["dbo.Clockings"]);
        Assert.Equal(17, counts["dbo.__EFMigrationsHistory"]);
        Assert.Equal(4, counts["platform.TenantMemberships"]);
    }

    [Fact]
    public async Task Inspection_04_Clean_Seed_Has_No_Problems()
    {
        var summary = Summary((await RunAsync(_app.ConnectionString, "04-data-quality.sql"))[0]);

        Assert.Equal(0, summary["Emails used by more than one account (NormalizedEmail)"]);
        Assert.Equal(0, summary["Accounts with no organization that are not SuperAdmin"]);
        Assert.Equal(0, summary["Volunteer sessions whose volunteer is in another organization"]);
        Assert.Equal(0, summary["Staff sessions whose staff member is in another organization"]);
        Assert.Equal(0, summary["People with more than one volunteer session on the same ClockDate"]);
        Assert.Equal(0, summary["People with more than one staff session on the same ClockDate"]);
        Assert.Equal(0, summary["Sessions with ClockDate 0001-01-01 (moved by the Move buttons, finding F-01/F-02)"]);
        // The house organization (from the migrations) and Gamma have no admin.
        Assert.Equal(2, summary["Organizations with no admin account"]);
        Assert.Equal(0, summary["Accounts without the Admin or SuperAdmin role"]);
        Assert.Equal(4, summary["Tenants (rows)"]);
        // Migration Phase6b seeds a placeholder subscription for the house organization.
        Assert.Equal(1, summary["Subscriptions (rows)"]);
        Assert.Equal(0, summary["Invoices (rows)"]);
    }

    [Fact]
    public async Task Inspection_05_Report_Baseline_Matches_The_Seed()
    {
        // Per Alpha person 26h, per Beta person 25.5h (see ReportTotalsTests); 3 people each.
        var results = await RunAsync(_app.ConnectionString, "05-report-baseline.sql");
        var alpha = _app.Data.Alpha.Id;
        var beta = _app.Data.Beta.Id;

        foreach (var report in new[] { results[0], results[1] })
        {
            var alphaMarch = Assert.Single(report, r => Convert.ToInt32(r["TenantId"]) == alpha);
            var betaMarch = Assert.Single(report, r => Convert.ToInt32(r["TenantId"]) == beta);
            Assert.Equal((2026, 3, 12, 0, 78L * 60), Row(alphaMarch));
            Assert.Equal((2026, 3, 12, 0, 765L * 6), Row(betaMarch));
        }

        var r3Alpha = Assert.Single(results[2], r => Convert.ToInt32(r["TenantId"]) == alpha);
        Assert.Equal((3, 3, 2, 12, 12, 78L * 60),
            (Convert.ToInt32(r3Alpha["Staff"]), Convert.ToInt32(r3Alpha["Volunteers"]), Convert.ToInt32(r3Alpha["Admins"]),
             Convert.ToInt32(r3Alpha["StaffSessions"]), Convert.ToInt32(r3Alpha["VolunteerSessions"]), Convert.ToInt64(r3Alpha["StaffWorkedMinutesAllTime"])));
    }

    private static (int, int, int, int, long) Row(Dictionary<string, object?> r) =>
        (Convert.ToInt32(r["Year"]), Convert.ToInt32(r["Month"]), Convert.ToInt32(r["Sessions"]),
         Convert.ToInt32(r["OpenSessions"]), Convert.ToInt64(r["WorkedMinutes"]));
}

/// <summary>Plants one of each problem 04-data-quality.sql looks for and checks it is reported.</summary>
public class InspectionAnomalyTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public InspectionAnomalyTests(AppFixture app) => _app = app;

    [Fact]
    public async Task Inspection_04_Reports_Each_Planted_Problem()
    {
        await using (var db = _app.CreateDbContext())
        {
            // An account with no organization or role.
            db.Users.Add(new AppUser { UserName = "orphan@nowhere.test", NormalizedUserName = "ORPHAN@NOWHERE.TEST", Email = "orphan@nowhere.test", NormalizedEmail = "ORPHAN@NOWHERE.TEST" });
            // A moved session with no date. (A session filed under another organization than its
            // person can no longer be planted: the composite keys of migration M4 refuse it - see below.)
            db.Clockings.Add(new Clocking { TenantId = _app.Data.Alpha.Id, VoluntId = _app.Data.Alpha.Volunteers[0].Id, ClockDate = DateTime.MinValue });
            await db.SaveChangesAsync();
        }

        var results = await RunAsync(_app.ConnectionString, "04-data-quality.sql");
        var summary = Summary(results[0]);

        Assert.Equal(1, summary["Accounts with no organization that are not SuperAdmin"]);
        Assert.Equal(0, summary["Staff sessions whose staff member is in another organization"]);
        Assert.Equal(0, summary["Volunteer sessions whose volunteer is in another organization"]);
        Assert.Equal(1, summary["Sessions with ClockDate 0001-01-01 (moved by the Move buttons, finding F-01/F-02)"]);
        Assert.Equal(1, summary["Accounts without the Admin or SuperAdmin role"]);
        Assert.Equal("ORPHAN@NOWHERE.TEST", Assert.Single(results[2])["NormalizedEmail"]);
        Assert.Empty(results[3]);
    }

    [Fact]
    public async Task Inspection_04_Cross_Organization_Sessions_Can_No_Longer_Be_Planted()
    {
        // Since migration M4 (Phase 0 step 0.5) a session's (TenantId, person) must match the
        // person's own organization, so checks 3 and 4 can only find problems in databases from before M4.
        await using var db = _app.CreateDbContext();
        db.ClockingsStaff.Add(new ClockingStaff { TenantId = _app.Data.Alpha.Id, StafId = _app.Data.Beta.Staff[0].Id, ClockDate = new DateTime(2026, 4, 2) });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Contains("FK_ClockingsStaff_Staffs_TenantId_StafId", error.InnerException!.Message);
    }

    [Fact]
    public async Task Inspection_04_Shared_Emails_Can_No_Longer_Be_Planted()
    {
        // Since migration M3 (Phase 0 step 0.4) the database itself refuses a second account with
        // the same email, so check 1 can only find problems in databases from before M3.
        var shared = _app.Data.Beta.Admin!.Email;
        await using var db = _app.CreateDbContext();
        db.Users.Add(new AppUser { UserName = shared, NormalizedUserName = shared.ToUpperInvariant(), Email = shared, NormalizedEmail = shared.ToUpperInvariant(), TenantId = _app.Data.Alpha.Id });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Contains("EmailIndex", error.InnerException!.Message);
        Assert.Equal(0, Summary((await RunAsync(_app.ConnectionString, "04-data-quality.sql"))[0])["Emails used by more than one account (NormalizedEmail)"]);
    }
}
