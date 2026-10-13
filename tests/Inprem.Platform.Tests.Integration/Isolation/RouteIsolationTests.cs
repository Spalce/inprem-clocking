using System.Net;
using System.Text;
using System.Text.Json;
using Inprem.Platform.Domain.Memberships;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Inprem.Platform.Tests.Integration.Isolation;

/// <summary>
/// Tenant-isolation suite (TESTING.md; Phase 0 step 0.5). Every route in the endpoint table is
/// called by an Alpha admin with Beta's ids - in the route, in the query string (id, staffId,
/// volunteerId), and in JSON and form bodies for every POST handler of every page and every POST
/// action. Afterwards no response may contain Beta's people or emails, and every Beta row must be
/// exactly as before (fingerprint). New routes are picked up automatically, and the test fails if
/// any route of the table was not exercised.
///
/// Deviation from the step text ("→ 403/404"): many approved (C1) handlers answer a foreign id with
/// their usual "not found" response - a redirect to the list, 200 with an empty result or a message,
/// or 500 when the save interceptor refuses - and cannot be changed in Phase 0. What this suite
/// guarantees is the substance: nothing of Beta's is revealed or changed.
/// </summary>
public class RouteIsolationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;
    private readonly ITestOutputHelper _output;

    public RouteIsolationTests(AppFixture app, ITestOutputHelper output)
    {
        _app = app;
        _output = output;
    }

    [Fact]
    public async Task Isolation_Every_Route_With_Beta_Ids_Reveals_And_Changes_Nothing_Of_Beta()
    {
        var probe = await CreateProbeAdminAsync();
        var ids = await BetaIdsAsync();
        var before = await BetaFingerprintAsync();
        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var exercised = new HashSet<RouteEndpoint>();
        var calls = 0;

        foreach (var endpoint in endpoints)
        {
            var pattern = endpoint.RoutePattern.RawText ?? "";
            foreach (var path in Paths(pattern, ids))
            {
                foreach (var (method, url, content) in Requests(endpoint, path, ids))
                {
                    // A fresh session per request: some routes sign out or change the session.
                    using var client = _app.CreateClient();
                    await SignIn.AsAsync(client, probe, _app.Data.Password);
                    using var request = new HttpRequestMessage(method, url) { Content = content() };
                    using var response = await client.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();

                    foreach (var marker in BetaMarkers())
                    {
                        Assert.False(body.Contains(marker, StringComparison.OrdinalIgnoreCase),
                            $"{method} {url} ({(int)response.StatusCode}) reveals Beta's '{marker}'.");
                    }
                    exercised.Add(endpoint);
                    calls++;
                }
            }
        }

        _output.WriteLine($"{endpoints.Count} routes, {calls} requests.");
        Assert.Equal(endpoints.Count, exercised.Count);
        Assert.True(endpoints.Count > 60, $"Only {endpoints.Count} routes found; the discovery looks broken.");
        Assert.Equal(before, await BetaFingerprintAsync());
    }

    private sealed record BetaIds(long Staff, long Volunteer, long StaffClocking, long VolunteerClocking, string StaffEmail, string VolunteerEmail, int Tenant, Guid Membership);

    private async Task<BetaIds> BetaIdsAsync()
    {
        await using var db = _app.CreateDbContext();
        await using var platform = _app.CreatePlatformDbContext();
        var staff = _app.Data.Beta.Staff[0];
        var volunteer = _app.Data.Beta.Volunteers[0];
        return new BetaIds(
            staff.Id, volunteer.Id,
            await db.ClockingsStaff.IgnoreQueryFilters().Where(c => c.StafId == staff.Id).Select(c => c.ClockingStaffId).FirstAsync(),
            await db.Clockings.IgnoreQueryFilters().Where(c => c.VoluntId == volunteer.Id).Select(c => c.ClockingId).FirstAsync(),
            staff.Email, volunteer.Email, _app.Data.Beta.Id,
            await platform.TenantMemberships.Where(m => m.TenantId == _app.Data.Beta.Id).Select(m => m.Id).FirstAsync());
    }

    /// <summary>The route with its parameters filled with Beta ids (one path per kind of id).</summary>
    private static IEnumerable<string> Paths(string pattern, BetaIds ids)
    {
        var parameters = System.Text.RegularExpressions.Regex.Matches(pattern, @"\{([^}:?=*]+)[^}]*\}").Select(m => m.Groups[1].Value).ToList();
        if (parameters.Count == 0)
        {
            yield return "/" + pattern.TrimStart('/');
            yield break;
        }

        var choices = new[] { ids.Staff.ToString(), ids.Volunteer.ToString(), ids.StaffEmail, ids.Tenant.ToString() };
        foreach (var value in choices.Distinct())
        {
            yield return "/" + System.Text.RegularExpressions.Regex.Replace(pattern, @"\{[^}]+\}", Uri.EscapeDataString(value)).TrimStart('/');
        }
    }

    private static IEnumerable<(HttpMethod Method, string Url, Func<HttpContent?> Content)> Requests(RouteEndpoint endpoint, string path, BetaIds ids)
    {
        var queries = new[]
        {
            "", $"?id={ids.Staff}&type=staff", $"?id={ids.Volunteer}&type=volunteer",
            $"?staffId={ids.Staff}&volunteerId={ids.Volunteer}&start=2026-03-01&end=2026-03-31",
        };

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET", "POST" };
        if (methods.Contains("GET"))
        {
            foreach (var query in queries) yield return (HttpMethod.Get, path + query, () => null);
        }

        var handlers = PostHandlers(endpoint);
        if (handlers.Count == 0 && methods.Contains("POST")) handlers.Add(null);
        foreach (var handler in handlers)
        {
            var url = handler == null ? path : $"{path}?handler={handler}";
            yield return (HttpMethod.Post, url, () => new StringContent(JsonBody(ids), Encoding.UTF8, "application/json"));
            yield return (HttpMethod.Post, url, () => new FormUrlEncodedContent(FormBody(ids)));
        }
    }

    /// <summary>POST handler names of a page (OnPostXxxAsync → "Xxx"; OnPost(Async) → null), or none for controllers.</summary>
    private static List<string?> PostHandlers(RouteEndpoint endpoint)
    {
        var page = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.CompiledPageActionDescriptor>();
        if (page == null) return new List<string?>();

        return (page.HandlerMethods ?? Array.Empty<Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure.HandlerMethodDescriptor>())
            .Where(h => string.Equals(h.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            .Select(h => string.IsNullOrEmpty(h.Name) ? null : h.Name)
            .Distinct().ToList();
    }

    private static string JsonBody(BetaIds ids) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["StaffId"] = ids.Staff, ["StafId"] = ids.Staff, ["VolunteerId"] = ids.Volunteer, ["VoluntId"] = ids.Volunteer,
        ["ClockingStaffId"] = ids.StaffClocking, ["ClockingId"] = ids.VolunteerClocking, ["Id"] = ids.Staff,
        ["EmailAddress"] = "probe-" + Guid.NewGuid().ToString("N")[..8] + "@alpha.test", ["FirstName"] = "Probe", ["LastName"] = "Person",
        ["Gender"] = "F", ["PhoneNumber"] = "6145550999", ["ZipCode"] = "43229", ["Address"] = "1 Probe Road, Testville",
        ["VolunteerCategory"] = "Personal or Individual Volunteering",
        ["ClockInTime"] = "2026-03-26T08:00", ["ClockOutTime"] = "2026-03-26T09:00",
        ["StartDate"] = "2026-03-01", ["EndDate"] = "2026-03-31", ["membershipId"] = ids.Membership,
    });

    private static Dictionary<string, string> FormBody(BetaIds ids) => new()
    {
        ["id"] = ids.Staff.ToString(), ["membershipId"] = ids.Membership.ToString(),
        ["ClockingStaff.StafId"] = ids.Staff.ToString(), ["ClockingVolunteerProp.VoluntId"] = ids.Volunteer.ToString(),
        // Ids are Beta's; free text is not (a create form would otherwise store Beta's email as a new
        // Alpha row, which is legitimate but would look like a leak to the marker check).
        ["Input.EmailAddress"] = "probe-" + Guid.NewGuid().ToString("N")[..8] + "@alpha.test", ["Input.FirstName"] = "Probe", ["Input.LastName"] = "Person",
        ["Input.ZipCode"] = "43229", ["Input.Gender"] = "F", ["Input.PhoneNumber"] = "6145550998",
    };

    private IEnumerable<string> BetaMarkers() =>
        _app.Data.Beta.Staff.Concat(_app.Data.Beta.Volunteers).SelectMany(p => new[] { p.Email, p.FullName })
            .Concat(new[] { "owner@beta.test", "admin@beta.test" });

    private async Task<string> CreateProbeAdminAsync()
    {
        var email = $"probe-{Guid.NewGuid():N}"[..20] + "@alpha.test";
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = _app.Data.Alpha.Id, Type = IdentitySeeder.AdminRole };
        Assert.True((await users.CreateAsync(user, _app.Data.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, IdentitySeeder.AdminRole)).Succeeded);
        await using var platform = _app.CreatePlatformDbContext();
        platform.TenantMemberships.Add(TenantMembership.CreateActive(user.Id, _app.Data.Alpha.Id, MembershipRole.Admin, DateTime.UtcNow));
        await platform.SaveChangesAsync();
        return email;
    }

    /// <summary>Every Beta row in every tenant-owned table and Beta's memberships, in a stable order.</summary>
    private async Task<string> BetaFingerprintAsync()
    {
        var beta = _app.Data.Beta.Id;
        await using var db = _app.CreateDbContext();
        await using var platform = _app.CreatePlatformDbContext();
        var sb = new StringBuilder();
        foreach (var s in await db.Staffs.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.StaffId).ToListAsync())
            sb.AppendLine(JsonSerializer.Serialize(new { s.StaffId, s.EmailAddress, s.FirstName, s.LastName, s.PhoneNumber, s.ZipCode, s.Address, s.Gender, s.Type }));
        foreach (var v in await db.Volunteers.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.VolunteerId).ToListAsync())
            sb.AppendLine(JsonSerializer.Serialize(new { v.VolunteerId, v.EmailAddress, v.FirstName, v.LastName, v.PhoneNumber, v.VolunteerCategory, v.Type }));
        foreach (var c in await db.ClockingsStaff.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.ClockingStaffId).ToListAsync())
            sb.AppendLine(JsonSerializer.Serialize(new { c.ClockingStaffId, c.StafId, c.ClockInTime, c.LeaveOnBreakTime, c.ReturnOnBreakTime, c.ClockOutTime, c.WorkingHours, c.ClockDate }));
        foreach (var c in await db.Clockings.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.ClockingId).ToListAsync())
            sb.AppendLine(JsonSerializer.Serialize(new { c.ClockingId, c.VoluntId, c.ClockInTime, c.LeaveOnBreakTime, c.ReturnOnBreakTime, c.ClockOutTime, c.WorkingHours, c.ClockDate }));
        foreach (var u in await db.Users.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.Id).ToListAsync())
            sb.AppendLine(JsonSerializer.Serialize(new { u.Id, u.Email, u.PasswordHash, u.SecurityStamp, u.LockoutEnd }));
        foreach (var m in await platform.TenantMemberships.Where(x => x.TenantId == beta).OrderBy(x => x.Id).ToListAsync())
            sb.AppendLine(JsonSerializer.Serialize(new { m.Id, m.UserId, m.Role, m.Status }));
        sb.AppendLine(JsonSerializer.Serialize(await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == beta).Select(t => new { t.Name, t.IsActive, t.Address, t.TimeZoneId }).SingleAsync()));
        return sb.ToString();
    }
}
