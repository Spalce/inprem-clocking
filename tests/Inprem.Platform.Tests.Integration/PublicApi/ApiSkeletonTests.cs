using System.Net;
using System.Text;
using Inprem.Platform.Domain.PublicApi;
using InpremClockingApp.PublicApi;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Inprem.Platform.Tests.Integration.PublicApi;

/// <summary>
/// Phase 3 step 3.1 (P3-2, P3-3, P3-9): the website API's skeleton through the real app: health,
/// tokens (and the back-office cookie ignored), CORS for the website only, rate limits with
/// Retry-After, problem details with stable codes, idempotency, and the generated OpenAPI document
/// against the agreed draft.
/// </summary>
public class ApiSkeletonTests : IClassFixture<PublicApiFixture>
{
    private const string Api = "/api/public/v1";
    private readonly PublicApiFixture _app;

    public ApiSkeletonTests(PublicApiFixture app) => _app = app;

    private static HttpRequestMessage Get(string path, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        if (token != null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Post(string path, string json, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Api + path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key != null) request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var json = await PublicApiFixture.JsonAsync(response);
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.Equal((int)status, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        Assert.EndsWith("/problems/" + code, json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Health_Answers_Without_Any_Credential()
    {
        using var client = _app.CreateClient();
        var response = await client.SendAsync(Get("/health"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", (await PublicApiFixture.JsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Tokens_Are_Required_Checked_And_Scoped_And_The_Cookie_Is_Ignored()
    {
        using var client = _app.CreateClient();
        await AssertProblemAsync(await client.SendAsync(Get("/test/whoami")), HttpStatusCode.Unauthorized, Problems.TokenMissing);
        await AssertProblemAsync(await client.SendAsync(Get("/test/whoami", "not-a-real-token-but-long-enough")), HttpStatusCode.Unauthorized, Problems.TokenInvalid);

        var signup = await _app.TokenAsync();
        var me = await PublicApiFixture.JsonAsync(await client.SendAsync(Get("/test/whoami", signup)));
        Assert.Equal("Signup", me.GetProperty("kind").GetString());

        // A signup token cannot use billing endpoints; a billing token can.
        await AssertProblemAsync(await client.SendAsync(Get("/test/billing", signup)), HttpStatusCode.Unauthorized, Problems.TokenInvalid);
        var billing = await _app.TokenAsync(billing: true, tenantId: _app.Data.Alpha.Id);
        var asBilling = await PublicApiFixture.JsonAsync(await client.SendAsync(Get("/test/whoami", billing)));
        Assert.Equal(("BillingAccess", _app.Data.Alpha.Id), (asBilling.GetProperty("kind").GetString(), asBilling.GetProperty("tenant").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/test/billing", billing))).StatusCode);

        // Expired after 30 minutes.
        _app.Clock.Advance(ApiToken.BillingAccessLifetime + TimeSpan.FromSeconds(1));
        try
        {
            await AssertProblemAsync(await client.SendAsync(Get("/test/billing", billing)), HttpStatusCode.Unauthorized, Problems.TokenExpired);
        }
        finally
        {
            _app.Clock.Advance(-(ApiToken.BillingAccessLifetime + TimeSpan.FromSeconds(1)));
        }

        // Signed in to the back office with a cookie, but no token: refused.
        using var browser = _app.CreateBrowserClient();
        await SignIn.AsAsync(browser, _app.Data.Alpha.Admin!.Email, _app.Data.Password);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/BackOffice")).StatusCode); // the cookie works there
        await AssertProblemAsync(await browser.SendAsync(Get("/test/whoami")), HttpStatusCode.Unauthorized, Problems.TokenMissing);

        // Tokens are stored only as hashes.
        await using var db = _app.CreatePlatformDbContext();
        Assert.False(await db.ApiTokens.AnyAsync(t => t.TokenHash == signup || t.TokenHash == billing));
    }

    [Fact]
    public async Task Cors_Allows_Only_The_Website_And_Never_Credentials()
    {
        using var client = _app.CreateClient();
        HttpRequestMessage Preflight(string origin, string path = "/test/create", string method = "POST")
        {
            var request = new HttpRequestMessage(HttpMethod.Options, Api + path);
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", method);
            request.Headers.Add("Access-Control-Request-Headers", "content-type,idempotency-key,authorization");
            return request;
        }

        var allowed = await client.SendAsync(Preflight(PublicApiFixture.WebsiteOrigin));
        Assert.True(allowed.Headers.Contains("Access-Control-Allow-Origin"), $"{(int)allowed.StatusCode} {string.Join("; ", allowed.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)))} {await allowed.Content.ReadAsStringAsync()}");
        Assert.Equal(PublicApiFixture.WebsiteOrigin, allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(allowed.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Contains("idempotency-key", string.Join(",", allowed.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant());

        var other = await client.SendAsync(Preflight("https://evil.test"));
        Assert.False(other.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.True((await client.SendAsync(Preflight(PublicApiFixture.WebsiteOrigin, "/health", "GET"))).Headers.Contains("Access-Control-Allow-Origin"));

        var get = Get("/health");
        get.Headers.Add("Origin", "https://evil.test");
        Assert.False((await client.SendAsync(get)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Too_Many_Requests_Get_429_With_Retry_After_And_An_Api_Key_Has_Its_Own_Limit()
    {
        using var client = _app.CreateClient("10.99.0.1");
        for (var i = 0; i < PublicApiFixture.PublicPerMinute; i++) Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Get("/health"))).StatusCode);
        var limited = await client.SendAsync(Get("/health"));
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, Problems.RateLimited);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single()) >= 1);

        // The website's server, from the same address, with its key: not limited at that rate.
        var withKey = Get("/health");
        withKey.Headers.Add("X-Api-Key", PublicApiFixture.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(withKey)).StatusCode);
        var wrongKey = Get("/health");
        wrongKey.Headers.Add("X-Api-Key", "not-the-key");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(wrongKey)).StatusCode);
    }

    [Fact]
    public async Task Idempotency_Same_Key_Same_Answer_Different_Body_422_And_Errors_Can_Be_Retried()
    {
        using var client = _app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", PublicApiFixture.ApiKey); // many requests: the website server's limit
        await AssertProblemAsync(await client.SendAsync(Post("/test/create", "{}", null)), HttpStatusCode.BadRequest, Problems.ValidationFailed);

        var key = Guid.NewGuid().ToString();
        var before = _app.Endpoints.Created;
        var first = await client.SendAsync(Post("/test/create", """{"a":1}""", key));
        var again = await client.SendAsync(Post("/test/create", """{"a":1}""", key));
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, again.StatusCode));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Equal(before + 1, _app.Endpoints.Created); // ran once

        await AssertProblemAsync(await client.SendAsync(Post("/test/create", """{"a":2}""", key)), HttpStatusCode.UnprocessableEntity, Problems.IdempotencyKeyReused);

        // At the same moment: still one.
        var concurrentKey = Guid.NewGuid().ToString();
        var counted = _app.Endpoints.Created;
        var all = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.SendAsync(Post("/test/create", """{"b":1}""", concurrentKey))));
        Assert.All(all, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(counted + 1, _app.Endpoints.Created);

        // 5xx answers are not kept: the same key runs again.
        var retryKey = Guid.NewGuid().ToString();
        var tries = _app.Endpoints.Created;
        await AssertProblemAsync(await client.SendAsync(Post("/test/unavailable", "{}", retryKey)), HttpStatusCode.ServiceUnavailable, Problems.PaymentProviderUnavailable);
        await client.SendAsync(Post("/test/unavailable", "{}", retryKey));
        Assert.Equal(tries + 2, _app.Endpoints.Created);

        // After 24 hours the key is free again.
        _app.Clock.Advance(IdempotencyRecord.Lifetime + TimeSpan.FromMinutes(1));
        try
        {
            var later = _app.Endpoints.Created;
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(Post("/test/create", """{"a":2}""", key))).StatusCode);
            Assert.Equal(later + 1, _app.Endpoints.Created);
        }
        finally
        {
            _app.Clock.Advance(-(IdempotencyRecord.Lifetime + TimeSpan.FromMinutes(1)));
        }
    }

    [Fact]
    public async Task Errors_Never_Leak_Details()
    {
        using var client = _app.CreateClient();
        var boom = await client.SendAsync(Get("/test/boom"));
        await AssertProblemAsync(boom, HttpStatusCode.InternalServerError, Problems.InternalError);
        Assert.DoesNotContain("secret detail", await boom.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_Is_Published_And_Matches_The_Agreed_Draft()
    {
        using var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(PublicApiHosting.DocsPath + "/" + PublicApiHosting.DocumentName + "/openapi.json")).StatusCode);

        var generated = _app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger(PublicApiHosting.DocumentName);
        var draft = Draft();
        var draftPaths = (YamlMappingNode)draft["paths"];
        var checkedOperations = 0;
        foreach (var (path, item) in generated.Paths)
        {
            var relative = path[Api.Length..];
            if (relative.StartsWith("/test/", StringComparison.Ordinal)) continue; // the fixture's endpoints
            Assert.True(draftPaths.Children.ContainsKey(new YamlScalarNode(relative)), $"{relative} is not in the agreed draft.");
            var draftItem = (YamlMappingNode)draftPaths[relative];
            foreach (var (method, operation) in item.Operations)
            {
                var verb = method.ToString().ToLowerInvariant();
                Assert.True(draftItem.Children.ContainsKey(new YamlScalarNode(verb)), $"{verb.ToUpperInvariant()} {relative} is not in the agreed draft.");
                var draftOperation = (YamlMappingNode)draftItem[verb];
                Assert.Equal(((YamlScalarNode)draftOperation["operationId"]).Value, operation.OperationId);
                var draftCodes = ((YamlMappingNode)draftOperation["responses"]).Children.Keys.Select(k => ((YamlScalarNode)k).Value).ToHashSet();
                Assert.All(operation.Responses.Keys, code => Assert.Contains(code, draftCodes));
                checkedOperations++;
            }
        }
        Assert.True(checkedOperations >= 1);

        // The problem codes: exactly the agreed list.
        var codes = ((YamlSequenceNode)((YamlMappingNode)((YamlMappingNode)((YamlMappingNode)((YamlMappingNode)draft["components"])["schemas"])["Problem"])["properties"])["code"]["enum"])
            .Children.Select(c => ((YamlScalarNode)c).Value).ToHashSet();
        Assert.Equal(codes.OrderBy(c => c), Problems.All.OrderBy(c => c));
    }

    private static YamlMappingNode Draft()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(Path.Combine(dir!.FullName, "docs", "multitenancy", "api", "public-v1.draft.yaml"))));
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }
}
