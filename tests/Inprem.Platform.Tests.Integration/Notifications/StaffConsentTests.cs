using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Notifications;

/// <summary>
/// Phase 6 step 6.2 (C3-8; P6-1, P6-2): the "text me reminders" box on the kiosk registration and the
/// back-office editors appears only when the organization switched staff alerts on; a ticked box is
/// recorded with its wording, for the number on the form, which needs its country code. Alerts off:
/// the forms are as before (and the baseline suite is unchanged).
/// </summary>
public class StaffConsentTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public StaffConsentTests(ProviderFixture app) => _app = app;

    private async Task<HttpClient> SignedInAsync(TenantSeed tenant)
    {
        var client = _app.CreateClient();
        await SignIn.AsAsync(client, tenant.Owner!.Email, _app.Data.Password);
        return client;
    }

    private async Task AlertsAsync(TenantSeed tenant, bool on)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StaffAlertSettings>().SaveAsync(tenant.Id,
            StaffAlertChoices.Defaults with { Enabled = on }, "test");
    }

    /// <summary>Posts the kiosk registration form (the form holding the Register button) with the given fields.</summary>
    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string page, params (string Name, string Value)[] fields)
    {
        var document = await GetDocumentAsync(client, page);
        var form = (IHtmlFormElement)document.GetElementById("registerSubmit")!.Closest("form")!;
        var values = form.Elements.OfType<IHtmlInputElement>().Where(i => i.Type == "hidden" && !string.IsNullOrEmpty(i.Name))
            .Select(i => new KeyValuePair<string, string>(i.Name!, i.Value)).ToList();
        values.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
        var action = string.IsNullOrEmpty(form.GetAttribute("action")) ? page : form.GetAttribute("action")!;
        return await client.PostAsync(action, new FormUrlEncodedContent(values));
    }

    /// <summary>A unique name each time (letters only, as the form requires): the kiosk asks about a second person with the same name.</summary>
    private static string Letters() => new(Enumerable.Range(0, 8).Select(_ => (char)('a' + Random.Shared.Next(26))).Prepend('K').ToArray());

    private static (string, string)[] Person(string email, string phone) => new[]
    {
        ("Input.FirstName", Letters()), ("Input.LastName", Letters()), ("Input.EmailAddress", email), ("Input.ZipCode", "43229"),
        ("Input.PhoneNumber", phone), ("Input.Gender", "M"),
    };

    private static string Email() => $"kc-{Guid.NewGuid():N}"[..13] + "@kiosk.test";

    private async Task<List<ConsentRecord>> ConsentsAsync(string phoneE164)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.ConsentRecords.AsNoTracking().Where(c => c.Phone == phoneE164).ToListAsync();
    }

    [Fact]
    public async Task With_Alerts_Off_The_Forms_Have_No_Box_And_A_Forged_Tick_Records_Nothing()
    {
        using var client = await SignedInAsync(_app.Data.Alpha);
        foreach (var page in new[] { "/StaffAttendance", "/VolunteerAttendance", "/Staff", "/Volunteer" })
            Assert.Null((await GetDocumentAsync(client, page)).QuerySelector("[data-testid=sms-consent]"));

        var phone = "+1202555" + Random.Shared.Next(1000, 9999);
        var registered = await RegisterAsync(client, "/StaffAttendance", Person(Email(), phone).Append(("SmsConsent", "true")).ToArray());
        Assert.StartsWith("/staff-clockin/", Location(registered));
        Assert.Empty(await ConsentsAsync(phone));
    }

    [Fact]
    public async Task With_Alerts_On_A_Ticked_Kiosk_Registration_Is_Recorded_With_Its_Wording()
    {
        var beta = _app.Data.Beta;
        await AlertsAsync(beta, on: true);
        try
        {
            using var client = await SignedInAsync(beta);
            foreach (var page in new[] { "/StaffAttendance", "/VolunteerAttendance", "/Staff", "/Volunteer" })
                Assert.NotNull((await GetDocumentAsync(client, page)).QuerySelector("[data-testid=sms-consent-box]"));
            Assert.Null((await GetDocumentAsync(client, "/StaffAttendance")).QuerySelector("[data-testid=sms-consent-box]")!.GetAttribute("checked")); // unticked

            var phone = "+1202555" + Random.Shared.Next(1000, 9999);
            var registered = await RegisterAsync(client, "/StaffAttendance", Person(Email(), phone[..2] + " " + phone[2..5] + " " + phone[5..]).Append(("SmsConsent", "true")).ToArray());
            var id = long.Parse(Location(registered)["/staff-clockin/".Length..]);
            var consent = Assert.Single(await ConsentsAsync(phone));
            Assert.Equal((ConsentAction.Grant, beta.Id, $"staff:{id}", ConsentSources.KioskRegistration, ConsentWording.StaffAlerts),
                (consent.Action, consent.TenantId, consent.PersonRef, consent.Source, consent.Wording));

            // Unticked: registered, nothing recorded.
            var other = "+1202555" + Random.Shared.Next(1000, 9999);
            Assert.StartsWith("/volunteer-clockin/", Location(await RegisterAsync(client, "/VolunteerAttendance",
                Person(Email(), other).Append(("Input.VolunteerCategory", "Personal or Individual Volunteering")).ToArray())));
            Assert.Empty(await ConsentsAsync(other));
        }
        finally
        {
            await AlertsAsync(beta, on: false);
        }
    }

    [Fact]
    public async Task A_Number_Without_Its_Country_Code_Is_Not_Recorded_But_The_Person_Is_Registered()
    {
        var beta = _app.Data.Beta;
        await AlertsAsync(beta, on: true);
        try
        {
            using var client = await SignedInAsync(beta);
            var email = Email();
            var registered = await RegisterAsync(client, "/StaffAttendance", Person(email, "614 516 1812").Append(("SmsConsent", "true")).ToArray());
            Assert.StartsWith("/staff-clockin/", Location(registered));
            await using var db = _app.CreatePlatformDbContext();
            Assert.False(await db.ConsentRecords.AnyAsync(c => c.Source == ConsentSources.KioskRegistration && c.Phone.EndsWith("6145161812")));
        }
        finally
        {
            await AlertsAsync(beta, on: false);
        }
    }

    [Fact]
    public async Task The_Editors_Box_Is_Recorded_For_A_Person_Of_This_Organization_Only()
    {
        var beta = _app.Data.Beta;
        var phone = "+1202555" + Random.Shared.Next(1000, 9999);
        string email;
        await using (var app = _app.CreateDbContext())
        {
            var staff = await app.Staffs.IgnoreQueryFilters().FirstAsync(s => s.TenantId == beta.Id);
            staff.PhoneNumber = phone;
            email = staff.EmailAddress;
            await app.SaveChangesAsync();
        }
        using var client = await SignedInAsync(beta);

        var off = await client.PostAsJsonAsync("/api/staff-alerts/consent", new { kind = "staff", email });
        Assert.Contains("Staff alerts are off", await off.Content.ReadAsStringAsync());

        await AlertsAsync(beta, on: true);
        try
        {
            var on = await client.PostAsJsonAsync("/api/staff-alerts/consent", new { kind = "staff", email });
            Assert.Contains("\"recorded\":true", await on.Content.ReadAsStringAsync());
            var consent = Assert.Single(await ConsentsAsync(phone));
            Assert.Equal((beta.Id, ConsentSources.BackOffice), (consent.TenantId, consent.Source));

            // Another organization's person is not found from Beta.
            await using var app = _app.CreateDbContext();
            var alphaEmail = (await app.Staffs.IgnoreQueryFilters().FirstAsync(s => s.TenantId == _app.Data.Alpha.Id)).EmailAddress;
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/staff-alerts/consent", new { kind = "staff", email = alphaEmail })).StatusCode);
        }
        finally
        {
            await AlertsAsync(beta, on: false);
        }
    }
}
