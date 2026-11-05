using System.Net;
using System.Text;
using System.Text.Json;
using InpremClockingApp.Models;
using InpremClockingApp.PublicApi;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.PublicApi;

public sealed class VolunteerRegistrationTests : IClassFixture<PublicApiFixture>
{
    private const string Api = "/api/public/v1";
    private readonly PublicApiFixture _app;

    public VolunteerRegistrationTests(PublicApiFixture app) => _app = app;

    private static HttpRequestMessage Post(object? body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Api + "/volunteers")
        {
            Content = new StringContent(body == null ? "" : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return request;
    }

    private object ValidBody(int tenantId, string? email = null, string? captcha = FakeCaptchaVerifier.Pass) => new
    {
        tenantId,
        emailAddress = email ?? $"volunteer-{Guid.NewGuid():N}@example.test",
        firstName = "Ama",
        lastName = "Owusu",
        zipCode = "43229",
        gender = 'F',
        phoneNumber = $"555-{Random.Shared.Next(1000000, 9999999)}",
        address = "123 Main Street",
        volunteerCategory = VolunteerCategories.PersonalOrIndividualVolunteering,
        captchaToken = captcha,
        consent = true
    };

    [Fact]
    public async Task Valid_registration_returns_pending_and_is_persisted_for_the_requested_tenant()
    {
        using var client = _app.CreateClient();
        var response = await client.SendAsync(Post(ValidBody(_app.Data.Alpha.Id)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await PublicApiFixture.JsonAsync(response);
        Assert.Equal("pending_approval", json.GetProperty("status").GetString());
        var id = json.GetProperty("registrationId").GetInt64();

        using var scope = _app.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<InpremClockingApp.Data.ApplicationDbContext>();
        var volunteer = await db.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.VolunteerId == id);
        Assert.Equal(_app.Data.Alpha.Id, volunteer.TenantId);
        Assert.Equal(VolunteerApprovalStatus.Pending, volunteer.ApprovalStatus);
    }

    [Fact]
    public async Task Invalid_captcha_returns_a_problem()
    {
        using var client = _app.CreateClient();
        var response = await client.SendAsync(Post(ValidBody(_app.Data.Alpha.Id, captcha: "invalid")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Problems.CaptchaFailed, (await PublicApiFixture.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_required_fields_returns_field_validation_errors()
    {
        using var client = _app.CreateClient();
        var response = await client.SendAsync(Post(new
        {
            tenantId = _app.Data.Alpha.Id,
            emailAddress = "not-an-email",
            captchaToken = FakeCaptchaVerifier.Pass,
            consent = false
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await PublicApiFixture.JsonAsync(response);
        Assert.Equal(Problems.ValidationFailed, json.GetProperty("code").GetString());
        var fields = json.GetProperty("errors");
        Assert.True(fields.EnumerateObject().Any());
    }

    [Fact]
    public async Task Unknown_tenant_returns_not_found_without_creating_a_volunteer()
    {
        using var client = _app.CreateClient();
        var response = await client.SendAsync(Post(ValidBody(int.MaxValue)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Problems.TenantNotFound, (await PublicApiFixture.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Category_specific_fields_are_validated()
    {
        using var client = _app.CreateClient();
        var response = await client.SendAsync(Post(new
        {
            tenantId = _app.Data.Alpha.Id,
            emailAddress = $"education-{Guid.NewGuid():N}@example.test",
            firstName = "Ama",
            lastName = "Owusu",
            zipCode = "43229",
            gender = 'F',
            phoneNumber = "555-0100001",
            volunteerCategory = VolunteerCategories.EducationalPurposes,
            captchaToken = FakeCaptchaVerifier.Pass,
            consent = true
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await PublicApiFixture.JsonAsync(response)).GetProperty("errors");
        Assert.True(fields.TryGetProperty("InstitutionName", out _));
        Assert.True(fields.TryGetProperty("ContactPerson", out _));
        Assert.True(fields.TryGetProperty("ContactPersonEmail", out _));
    }

    [Fact]
    public async Task Duplicate_email_in_the_same_tenant_returns_conflict()
    {
        var email = $"duplicate-{Guid.NewGuid():N}@example.test";
        using var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Post(ValidBody(_app.Data.Alpha.Id, email)))).StatusCode);

        var response = await client.SendAsync(Post(ValidBody(_app.Data.Alpha.Id, email)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(Problems.VolunteerEmailAlreadyRegistered, (await PublicApiFixture.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Same_email_can_register_in_a_different_tenant()
    {
        var email = $"cross-tenant-{Guid.NewGuid():N}@example.test";
        using var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Post(ValidBody(_app.Data.Alpha.Id, email)))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Post(ValidBody(_app.Data.Beta.Id, email)))).StatusCode);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_the_first_response()
    {
        var key = Guid.NewGuid().ToString();
        var body = ValidBody(_app.Data.Alpha.Id);
        using var client = _app.CreateClient();
        var first = await client.SendAsync(Post(body, key));
        var second = await client.SendAsync(Post(body, key));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal((await PublicApiFixture.JsonAsync(first)).GetProperty("registrationId").GetInt64(),
            (await PublicApiFixture.JsonAsync(second)).GetProperty("registrationId").GetInt64());
    }

    [Fact]
    public async Task Reusing_idempotency_key_with_a_different_body_returns_unprocessable_entity()
    {
        var key = Guid.NewGuid().ToString();
        using var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Post(ValidBody(_app.Data.Alpha.Id), key))).StatusCode);

        var response = await client.SendAsync(Post(ValidBody(_app.Data.Alpha.Id), key));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(Problems.IdempotencyKeyReused, (await PublicApiFixture.JsonAsync(response)).GetProperty("code").GetString());
    }
}
