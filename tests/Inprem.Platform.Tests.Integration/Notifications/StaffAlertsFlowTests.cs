using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Models;
using InpremClockingApp.StaffAlerts;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Notifications;

/// <summary>
/// Phase 6 step 6.3 end to end, with the database (P6-1, P6-3): the job reads the open sessions of the
/// organizations that switched staff alerts on and texts the people who agreed, at each organization's
/// own local time. Alpha is in New York, Beta in London; 1 November 2026 is New York's clock-change day.
/// </summary>
public class StaffAlertsFlowTests : IClassFixture<ProviderFixture>
{
    private static readonly TimeOnly At2030 = new(20, 30);

    private readonly ProviderFixture _app;
    private TimeSpan _moved = TimeSpan.Zero;

    public StaffAlertsFlowTests(ProviderFixture app) => _app = app;

    private async Task<T> UseAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>Moves the app's clock to <paramref name="utc"/> (undone by <see cref="MoveBack"/>).</summary>
    private void MoveTo(DateTime utc)
    {
        var by = utc - _app.Clock.UtcNow;
        _app.Clock.Advance(by);
        _moved += by;
    }

    private void MoveBack()
    {
        _app.Clock.Advance(-_moved);
        _moved = TimeSpan.Zero;
    }

    /// <summary>One run of the job as Hangfire would start it, then the send job.</summary>
    private async Task RunJobAsync()
    {
        await UseAsync(async sp => { await sp.GetRequiredService<StaffAlertsJob>().RunAsync(CancellationToken.None); return 0; });
        await UseAsync(sp => sp.GetRequiredService<NotificationSender>().SendDueAsync());
    }

    private Task AlertsAsync(TenantSeed tenant, bool on, bool summary = false, int longBreak = 60) => UseAsync(async sp =>
        await sp.GetRequiredService<StaffAlertSettings>().SaveAsync(tenant.Id, new StaffAlertChoices(on, At2030, longBreak, summary), "test"));

    private static string UsPhone() => "+1202555" + Random.Shared.Next(1000, 9999);

    private static string UkPhone() => "+44770090" + Random.Shared.Next(1000, 9999);

    /// <summary>Gives the person a number and, when <paramref name="agreed"/>, a recorded consent for texts from the organization.</summary>
    private async Task PhoneAsync(TenantSeed tenant, PersonKind kind, long id, string phone, bool agreed)
    {
        await using (var db = _app.CreateDbContext())
        {
            if (kind == PersonKind.Staff) (await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id)).PhoneNumber = phone;
            else (await db.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.VolunteerId == id)).PhoneNumber = phone;
            await db.SaveChangesAsync();
        }
        if (!agreed) return;
        await using var platform = _app.CreatePlatformDbContext();
        platform.ConsentRecords.Add(ConsentRecord.Record(phone, ConsentAction.Grant, tenant.Id, StaffConsentRecorder.PersonRef(kind, id),
            ConsentSources.KioskRegistration, ConsentWording.StaffAlerts, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), "test"));
        await platform.SaveChangesAsync();
    }

    /// <summary>An open session (clocked in, not out) on the local day <paramref name="day"/>.</summary>
    private async Task ClockInAsync(TenantSeed tenant, PersonKind kind, long id, DateOnly day, DateTime clockInUtc, DateTime? breakUtc = null, DateTime? backUtc = null)
    {
        await using var db = _app.CreateDbContext();
        var date = day.ToDateTime(TimeOnly.MinValue);
        if (kind == PersonKind.Staff)
            db.ClockingsStaff.Add(new ClockingStaff { TenantId = tenant.Id, StafId = id, ClockInTime = clockInUtc, LeaveOnBreakTime = breakUtc, ReturnOnBreakTime = backUtc, CreatedAt = clockInUtc, ClockDate = date });
        else
            db.Clockings.Add(new Clocking { TenantId = tenant.Id, VoluntId = id, ClockInTime = clockInUtc, LeaveOnBreakTime = breakUtc, ReturnOnBreakTime = backUtc, CreatedAt = clockInUtc, ClockDate = date });
        await db.SaveChangesAsync();
    }

    private List<string> TextsTo(string phone) => _app.Sms.To(phone).Select(s => s.Body).ToList();

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task P6_3_Missed_Clock_Out_Is_Texted_At_Each_Organizations_Local_Time_On_A_Clock_Change_Day()
    {
        var (alpha, beta) = (_app.Data.Alpha, _app.Data.Beta);
        var day = new DateOnly(2026, 11, 1);
        var (alphaPhone, alphaNoConsent, betaPhone) = (UsPhone(), UsPhone(), UkPhone());
        await PhoneAsync(alpha, PersonKind.Staff, alpha.Staff[0].Id, alphaPhone, agreed: true);
        await PhoneAsync(alpha, PersonKind.Staff, alpha.Staff[1].Id, alphaNoConsent, agreed: false);
        await PhoneAsync(beta, PersonKind.Volunteer, beta.Volunteers[0].Id, betaPhone, agreed: true);
        await ClockInAsync(alpha, PersonKind.Staff, alpha.Staff[0].Id, day, Utc(11, 1, 14));      // 09:00 in New York (EST)
        await ClockInAsync(alpha, PersonKind.Staff, alpha.Staff[1].Id, day, Utc(11, 1, 14));
        await ClockInAsync(beta, PersonKind.Volunteer, beta.Volunteers[0].Id, day, Utc(11, 1, 9)); // 09:00 in London (GMT)
        await AlertsAsync(alpha, on: true);
        await AlertsAsync(beta, on: true);
        try
        {
            MoveTo(Utc(11, 1, 20, 25)); // London 20:25
            await RunJobAsync();
            Assert.Empty(TextsTo(betaPhone));

            MoveTo(Utc(11, 1, 20, 30)); // London 20:30; New York 15:30
            await RunJobAsync();
            Assert.Equal($"{beta.Name}: you are still clocked in (since 09:00). Please clock out at the kiosk, or tell an admin. Reply STOP to opt out.", Assert.Single(TextsTo(betaPhone)));
            Assert.Empty(TextsTo(alphaPhone));

            MoveTo(Utc(11, 2, 0, 30)); // 19:30 in New York since the clocks went back (it would be 20:30 the day before)
            await RunJobAsync();
            Assert.Empty(TextsTo(alphaPhone));

            MoveTo(Utc(11, 2, 1, 30)); // 20:30 in New York
            await RunJobAsync();
            Assert.Contains("(since 09:00)", Assert.Single(TextsTo(alphaPhone)));
            Assert.Empty(TextsTo(alphaNoConsent)); // no consent, no text

            MoveTo(Utc(11, 2, 1, 50)); // later runs: nothing twice
            await RunJobAsync();
            Assert.Single(TextsTo(alphaPhone));
            Assert.Single(TextsTo(betaPhone));

            await using var platform = _app.CreatePlatformDbContext();
            var skipped = await platform.Notifications.AsNoTracking()
                .SingleAsync(n => n.RecipientRef == StaffConsentRecorder.PersonRef(PersonKind.Staff, alpha.Staff[1].Id) && n.TemplateKey == NotificationTemplates.MissedClockOut);
            Assert.Equal((NotificationStatus.Skipped, SkipReasons.NoConsent, alpha.Id), (skipped.Status, skipped.SkipReason, skipped.TenantId));
        }
        finally
        {
            MoveBack();
            await AlertsAsync(alpha, on: false);
            await AlertsAsync(beta, on: false);
        }
    }

    [Fact]
    public async Task P6_1_With_Staff_Alerts_Off_Nobody_Is_Texted()
    {
        var alpha = _app.Data.Alpha;
        var day = new DateOnly(2026, 11, 5);
        var phone = UsPhone();
        await PhoneAsync(alpha, PersonKind.Volunteer, alpha.Volunteers[1].Id, phone, agreed: true);
        await ClockInAsync(alpha, PersonKind.Volunteer, alpha.Volunteers[1].Id, day, Utc(11, 5, 14), breakUtc: Utc(11, 5, 16));
        try
        {
            MoveTo(Utc(11, 6, 1, 35)); // 20:35 in New York, a long break too
            await RunJobAsync();
            Assert.Empty(TextsTo(phone));
            await using var platform = _app.CreatePlatformDbContext();
            Assert.False(await platform.Notifications.AnyAsync(n => n.RecipientRef == StaffConsentRecorder.PersonRef(PersonKind.Volunteer, alpha.Volunteers[1].Id)));
        }
        finally
        {
            MoveBack();
        }
    }

    [Fact]
    public async Task A_Long_Break_Is_Texted_Once_And_A_Finished_Break_Is_Not()
    {
        var beta = _app.Data.Beta;
        var day = new DateOnly(2026, 11, 4);
        var (onBreak, back) = (UkPhone(), UkPhone());
        await PhoneAsync(beta, PersonKind.Staff, beta.Staff[1].Id, onBreak, agreed: true);
        await PhoneAsync(beta, PersonKind.Staff, beta.Staff[2].Id, back, agreed: true);
        await ClockInAsync(beta, PersonKind.Staff, beta.Staff[1].Id, day, Utc(11, 4, 9), breakUtc: Utc(11, 4, 12));
        await ClockInAsync(beta, PersonKind.Staff, beta.Staff[2].Id, day, Utc(11, 4, 9), breakUtc: Utc(11, 4, 11), backUtc: Utc(11, 4, 11, 20));
        await AlertsAsync(beta, on: true, longBreak: 45);
        try
        {
            MoveTo(Utc(11, 4, 12, 40));
            await RunJobAsync();
            Assert.Empty(TextsTo(onBreak));

            MoveTo(Utc(11, 4, 12, 45));
            await RunJobAsync();
            MoveTo(Utc(11, 4, 13, 30));
            await RunJobAsync();
            Assert.Equal($"{beta.Name}: your break started at 12:00 and is still open. Please end it at the kiosk when you are back. Reply STOP to opt out.", Assert.Single(TextsTo(onBreak)));
            Assert.Empty(TextsTo(back));
        }
        finally
        {
            MoveBack();
            await AlertsAsync(beta, on: false);
        }
    }

    [Fact]
    public async Task The_Admins_Get_One_Summary_Email_For_The_Organization()
    {
        var alpha = _app.Data.Alpha;
        var day = new DateOnly(2026, 11, 3);
        foreach (var person in alpha.Staff) await ClockInAsync(alpha, PersonKind.Staff, person.Id, day, Utc(11, 3, 14));
        await AlertsAsync(alpha, on: true, summary: true);
        try
        {
            MoveTo(Utc(11, 4, 1, 30)); // 20:30 in New York
            await RunJobAsync();
            MoveTo(Utc(11, 4, 1, 40));
            await RunJobAsync();

            var summary = Assert.Single(_app.Emails.To(alpha.Owner!.Email), e => e.Subject.Contains("still clocked in"));
            Assert.Equal($"{alpha.Name}: {alpha.Staff.Count} still clocked in at 20:30", summary.Subject);
            foreach (var person in alpha.Staff) Assert.Contains($"{person.FirstName} {person.LastName}, since 09:00", summary.HtmlBody);
        }
        finally
        {
            MoveBack();
            await AlertsAsync(alpha, on: false);
        }
    }
}
