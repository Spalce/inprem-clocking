using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.PublicApi;
using Inprem.Platform.Infrastructure.Persistence;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.PublicApi;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.PublicApi;

/// <summary>
/// The real app with the website API configured as on staging (docs on, the website origin), low
/// rate limits, a movable clock, and a few test endpoints inside the API group
/// (<see cref="TestEndpoints"/>) that exercise its rules before the real endpoints exist.
/// </summary>
public class PublicApiFixture : IAsyncLifetime
{
    public const string WebsiteOrigin = "https://www.website.test";
    public const int PublicPerMinute = 5;
    public const string ApiKey = "test-website-server-key-0123456789";

    private TestDatabase _db = null!;
    private InpremAppFactory _app = null!;

    public TestClock Clock { get; } = new();
    public SeedCatalog Data { get; private set; } = null!;
    public IServiceProvider Services => _app.Services;
    public TestEndpoints Endpoints { get; } = new();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(GetType().Name);
        _app = new InpremAppFactory(_db.ConnectionString, new Dictionary<string, string?>
        {
            ["Site:SiteMode"] = "All",
            ["Site:PublicWebsiteOrigins:0"] = WebsiteOrigin,
            ["PublicApi:EnableDocs"] = "true",
            ["PublicApi:PublicPerMinute"] = PublicPerMinute.ToString(),
            ["PublicApi:TokenPerMinute"] = "1000",
            ["PublicApi:ApiKeyPerMinute"] = "1000",
            ["PublicApi:ApiKeyHashes:0"] = Inprem.Platform.Application.Provider.SecretTokens.Hash(ApiKey),
        }, configureServices: services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<IPublicApiEndpointContributor>(Endpoints);
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, TestAddressFilter>();
        });
        _ = _app.Server;
        Data = await TestDataSeeder.SeedAsync(_app.Services);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    /// <summary>A client from its own address (the limiter counts per address), so tests do not share limits.</summary>
    public HttpClient CreateClient(string? address = null)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Address", address ?? $"10.0.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}");
        return client;
    }

    public HttpClient CreateBrowserClient() => _app.CreateClient(); // shares the cookie container, like a browser

    public PlatformDbContext CreatePlatformDbContext() => _db.CreatePlatformDbContext();

    public async Task<string> TokenAsync(bool billing = false, int? tenantId = null)
    {
        using var scope = Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<PublicApiTokens>();
        var issued = billing
            ? await tokens.IssueBillingAccessAsync(tenantId ?? Data.Alpha.Id)
            : await tokens.IssueSignupAsync(Guid.NewGuid(), tenantId);
        return issued.Value;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}

/// <summary>The test server has no client address: X-Test-Address stands in for it (the limiter counts per address).</summary>
public sealed class TestAddressFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, following) =>
        {
            if (System.Net.IPAddress.TryParse(context.Request.Headers["X-Test-Address"].ToString(), out var address)) context.Connection.RemoteIpAddress = address;
            return following();
        });
        next(app);
    };
}

/// <summary>Test-only endpoints in the API group: who am I, billing only, an idempotent create, a failing create, a crash.</summary>
public sealed class TestEndpoints : IPublicApiEndpointContributor
{
    private int _created;

    public int Created => _created;

    public void Map(RouteGroupBuilder api)
    {
        api.MapGet("/test/whoami", (HttpContext http) => Results.Ok(new { kind = http.User.FindFirst(PublicApiClaims.Kind)?.Value, tenant = PublicApiClaims.TenantOf(http.User) }))
            .RequireAuthorization(PublicApiHosting.AnyTokenPolicy);
        api.MapGet("/test/billing", () => Results.Ok(new { ok = true })).RequireAuthorization(PublicApiHosting.BillingPolicy);
        api.MapPost("/test/create", (HttpContext http) =>
        {
            var n = Interlocked.Increment(ref _created);
            return Results.Json(new { n }, statusCode: StatusCodes.Status201Created);
        }).AllowAnonymous().RequireIdempotency();
        api.MapPost("/test/unavailable", (HttpContext http) =>
        {
            var n = Interlocked.Increment(ref _created);
            return Problems.Create(http, StatusCodes.Status503ServiceUnavailable, Problems.PaymentProviderUnavailable, $"Not now ({n}).");
        }).AllowAnonymous().RequireIdempotency();
        api.MapGet("/test/boom", () => { throw new InvalidOperationException("secret detail"); }).AllowAnonymous();
    }
}
