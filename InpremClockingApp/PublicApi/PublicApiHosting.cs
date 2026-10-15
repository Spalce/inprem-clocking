using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Application.PublicApi;
using InpremClockingApp.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace InpremClockingApp.PublicApi;

/// <summary>Configuration section <c>PublicApi</c> (Phase 3).</summary>
public sealed class PublicApiOptions
{
    public const string Section = "PublicApi";

    /// <summary>Publish the generated OpenAPI document at /api/public/docs (staging only).</summary>
    public bool EnableDocs { get; set; }

    /// <summary>SHA-256 hashes (hex) of the website server's X-Api-Key values (higher limits; never a substitute for the tokens).</summary>
    public string[] ApiKeyHashes { get; set; } = Array.Empty<string>();

    /// <summary>Requests per minute: per address without a key; per token; per API key; and for the write endpoints per address.</summary>
    public int PublicPerMinute { get; set; } = 60;
    public int TokenPerMinute { get; set; } = 120;
    public int ApiKeyPerMinute { get; set; } = 600;
    public int WritePerMinute { get; set; } = 10;
}

/// <summary>
/// The website API (Phase 3, design 7): <c>/api/public/v1</c> on the App site only.
/// <list type="bullet">
/// <item>Its own authentication: opaque bearer tokens (<see cref="PublicApiTokenHandler"/>); the
/// back-office cookie is never used (every protected endpoint names the token scheme).</item>
/// <item>CORS for the website origins only (<c>Site:PublicWebsiteOrigins</c>), without credentials.</item>
/// <item>Rate limits per address, per token and per API key; 429 with Retry-After.</item>
/// <item>Errors as <c>application/problem+json</c> with a stable <c>code</c> (<see cref="Problems"/>).</item>
/// <item>Idempotency for the endpoints that take an <c>Idempotency-Key</c> (<see cref="IdempotencyFilter"/>).</item>
/// <item>An OpenAPI document generated from the code, at /api/public/docs when <c>PublicApi:EnableDocs</c>.</item>
/// </list>
/// </summary>
public static class PublicApiHosting
{
    public const string Prefix = "/api/public/v1";
    public const string DocsPath = "/api/public/docs";
    public const string DocumentName = "public-v1";
    public const string CorsPolicy = "PublicWebsite";
    public const string RatePolicy = "public-api";
    public const string WriteRatePolicy = "public-api-write";
    public const string TokenScheme = "PublicApiToken";

    public const string SignupPolicy = "PublicApi.Signup";
    public const string BillingPolicy = "PublicApi.Billing";
    public const string AnyTokenPolicy = "PublicApi.AnyToken";

    public static IServiceCollection AddPublicApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PublicApiOptions>().Bind(configuration.GetSection(PublicApiOptions.Section));

        // Registration (step 3.3): the verification email and the CAPTCHA (Cloudflare Turnstile, D30).
        services.AddScoped<ISignupMail, SignupMail>();
        services.AddOptions<Inprem.Platform.Infrastructure.PublicApi.CaptchaOptions>().Bind(configuration.GetSection(Inprem.Platform.Infrastructure.PublicApi.CaptchaOptions.Section));
        services.AddHttpClient(Inprem.Platform.Infrastructure.PublicApi.TurnstileCaptchaVerifier.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<ICaptchaVerifier, Inprem.Platform.Infrastructure.PublicApi.TurnstileCaptchaVerifier>();

        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, PublicApiTokenHandler>(TokenScheme, _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(SignupPolicy, p => p.AddAuthenticationSchemes(TokenScheme).RequireClaim(PublicApiClaims.Kind, nameof(Inprem.Platform.Domain.PublicApi.ApiTokenKind.Signup)));
            options.AddPolicy(BillingPolicy, p => p.AddAuthenticationSchemes(TokenScheme).RequireClaim(PublicApiClaims.Kind, nameof(Inprem.Platform.Domain.PublicApi.ApiTokenKind.BillingAccess)));
            options.AddPolicy(AnyTokenPolicy, p => p.AddAuthenticationSchemes(TokenScheme).RequireClaim(PublicApiClaims.Kind));
        });

        services.AddCors();
        services.AddSingleton<IConfigureOptions<CorsOptions>, ConfigurePublicWebsiteCors>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Problems.Create(context.HttpContext, StatusCodes.Status429TooManyRequests, Problems.RateLimited, "Too many requests. Try again later.").ExecuteAsync(context.HttpContext);
            };
            options.AddPolicy(RatePolicy, context => Partition(context, write: false));
            options.AddPolicy(WriteRatePolicy, context => Partition(context, write: true));
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(DocumentName, new OpenApiInfo { Title = "InpremClockin Website API", Version = "1.0.0" });
            c.DocInclusionPredicate((document, api) => api.GroupName == document);
            c.AddSecurityDefinition("signupToken", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "Returned by POST /signups." });
            c.AddSecurityDefinition("billingAccessToken", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "From POST /billing-access/sessions." });
        });
        return services;
    }

    /// <summary>
    /// The limiter partition: a valid X-Api-Key gets its own, higher limit; a bearer token its own;
    /// everything else is counted per address. Write endpoints are always counted per address.
    /// </summary>
    private static RateLimitPartition<string> Partition(HttpContext context, bool write)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<PublicApiOptions>>().Value;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (write) return Fixed($"write:{ip}", options.WritePerMinute);

        var apiKey = context.Request.Headers["X-Api-Key"].ToString();
        if (apiKey.Length > 0 && options.ApiKeyHashes.Any(h => SecretTokens.Matches(apiKey, h)))
            return Fixed($"key:{SecretTokens.Hash(apiKey)[..16]}", options.ApiKeyPerMinute);

        var bearer = PublicApiTokenHandler.BearerValue(context.Request);
        if (bearer != null) return Fixed($"token:{SecretTokens.Hash(bearer)[..16]}", options.TokenPerMinute);

        return Fixed($"ip:{ip}", options.PublicPerMinute);
    }

    private static RateLimitPartition<string> Fixed(string key, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, perMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    /// <summary>Middleware: after UseRouting, before authentication.</summary>
    public static IApplicationBuilder UsePublicApi(this WebApplication app)
    {
        app.UseCors();
        app.UseRateLimiter();
        if (app.Services.GetRequiredService<IOptions<PublicApiOptions>>().Value.EnableDocs && app.Services.GetRequiredService<IOptions<SiteOptions>>().Value.ServesApp)
        {
            app.UseSwagger(c => c.RouteTemplate = DocsPath.TrimStart('/') + "/{documentName}/openapi.json");
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = DocsPath.TrimStart('/');
                c.SwaggerEndpoint($"{DocsPath}/{DocumentName}/openapi.json", "Website API v1");
            });
        }
        return app;
    }

    /// <summary>The endpoints, on deployments that serve the App site (on its host when one is set).</summary>
    public static IEndpointRouteBuilder MapPublicApi(this IEndpointRouteBuilder endpoints)
    {
        var site = endpoints.ServiceProvider.GetRequiredService<IOptions<SiteOptions>>().Value;
        if (!site.ServesApp) return endpoints;

        var group = endpoints.MapGroup(Prefix)
            .RequireCors(CorsPolicy)
            .RequireRateLimiting(RatePolicy)
            .WithGroupName(DocumentName)
            .WithMetadata(PublicApiEndpointMetadata.Instance)
            .AddEndpointFilter<ProblemOnExceptionFilter>();
        if (site.HostFor(SiteMode.App) is { } host) group.RequireHost(host);

        PublicApiEndpoints.Map(group);
        foreach (var contributor in endpoints.ServiceProvider.GetServices<IPublicApiEndpointContributor>()) contributor.Map(group);
        return endpoints;
    }
}

/// <summary>
/// Adds endpoints to the website API group, with all its rules (CORS, limits, token scheme, errors).
/// None is registered by the app itself; the tests use it to exercise the group's rules through
/// the real pipeline before the endpoints that need them exist.
/// </summary>
public interface IPublicApiEndpointContributor
{
    void Map(RouteGroupBuilder api);
}

/// <summary>Marks the website API's endpoints (no organization context, no back-office cookie).</summary>
public sealed class PublicApiEndpointMetadata
{
    public static readonly PublicApiEndpointMetadata Instance = new();
}

/// <summary>CORS for the website origins only, without credentials (design 7.7).</summary>
internal sealed class ConfigurePublicWebsiteCors : IConfigureOptions<CorsOptions>
{
    private readonly IOptions<SiteOptions> _site;

    public ConfigurePublicWebsiteCors(IOptions<SiteOptions> site) => _site = site;

    public void Configure(CorsOptions options) =>
        options.AddPolicy(PublicApiHosting.CorsPolicy, policy => policy
            .WithOrigins(_site.Value.PublicWebsiteOrigins.Select(o => o.TrimEnd('/')).ToArray())
            .WithMethods("GET", "POST")
            .WithHeaders("Content-Type", "Authorization", "Idempotency-Key", "X-Api-Key")
            .WithExposedHeaders("Retry-After")
            .SetPreflightMaxAge(TimeSpan.FromHours(1)));
}

public static class PublicApiClaims
{
    public const string TokenId = "public_api_token";
    public const string Kind = "public_api_kind";
    public const string TenantId = "public_api_tenant";
    public const string SignupId = "public_api_signup";

    public static Guid TokenIdOf(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(TokenId)!);

    public static int? TenantOf(ClaimsPrincipal user) => int.TryParse(user.FindFirstValue(TenantId), out var id) ? id : null;

    public static Guid? SignupOf(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(SignupId), out var id) ? id : null;
}

/// <summary>
/// <c>Authorization: Bearer &lt;token&gt;</c> for the website API. A missing, unknown or expired
/// token gets 401 with <c>token_missing</c>, <c>token_invalid</c> or <c>token_expired</c>.
/// </summary>
public sealed class PublicApiTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string FailureKey = "public_api_failure";
    private readonly PublicApiTokens _tokens;

    public PublicApiTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, PublicApiTokens tokens)
        : base(options, logger, encoder) => _tokens = tokens;

    public static string? BearerValue(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var (result, token) = await _tokens.CheckAsync(BearerValue(Request), Context.RequestAborted);
        if (result != TokenCheck.Valid)
        {
            Context.Items[FailureKey] = result;
            return result == TokenCheck.Missing ? AuthenticateResult.NoResult() : AuthenticateResult.Fail(result.ToString());
        }

        var claims = new List<Claim> { new(PublicApiClaims.TokenId, token!.Id.ToString()), new(PublicApiClaims.Kind, token.Kind.ToString()) };
        if (token.TenantId is int tenant) claims.Add(new Claim(PublicApiClaims.TenantId, tenant.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (token.SignupId is Guid signup) claims.Add(new Claim(PublicApiClaims.SignupId, signup.ToString()));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var failure = Context.Items.TryGetValue(FailureKey, out var value) && value is TokenCheck check ? check : TokenCheck.Missing;
        var (code, title) = failure switch
        {
            TokenCheck.Expired => (Problems.TokenExpired, "The token has expired."),
            TokenCheck.Invalid => (Problems.TokenInvalid, "The token is not valid."),
            _ => (Problems.TokenMissing, "A bearer token is required."),
        };
        Response.Headers.WWWAuthenticate = "Bearer";
        return Problems.Create(Context, StatusCodes.Status401Unauthorized, code, title).ExecuteAsync(Context);
    }

    /// <summary>A valid token of the wrong kind: as if it were not valid here.</summary>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Problems.Create(Context, StatusCodes.Status401Unauthorized, Problems.TokenInvalid, "This token cannot be used here.").ExecuteAsync(Context);
}
