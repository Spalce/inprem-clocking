using System.Globalization;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Organizations;
using InpremClockingApp.Provider.Organizations;
using Microsoft.Extensions.Caching.Memory;

namespace InpremClockingApp.PublicApi;

/// <summary>
/// The website API's endpoints (Phase 3). Public endpoints need no credential and return no
/// personal data (design 7.2).
/// </summary>
public static class PublicApiEndpoints
{
    public static readonly TimeSpan PlansCacheTime = TimeSpan.FromMinutes(5);
    private const string PlansCacheKey = "public-api:plans";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
            .AllowAnonymous()
            .WithName("getHealth")
            .WithTags("Public")
            .Produces<HealthResponse>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/plans", GetPlansAsync)
            .AllowAnonymous()
            .WithName("listPlans")
            .WithTags("Public")
            .Produces<IReadOnlyList<PlanResponse>>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/timezones", () => Results.Ok(OrganizationUi.TimeZones.Select(z => new TimeZoneResponse(z.Id, z.Label)).ToList()))
            .AllowAnonymous()
            .WithName("listTimeZones")
            .WithTags("Public")
            .Produces<IReadOnlyList<TimeZoneResponse>>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Registration (step 3.3).
        api.MapPost("/signups", CreateSignupAsync)
            .AllowAnonymous()
            .RequireRateLimiting(PublicApiHosting.WriteRatePolicy)
            .RequireIdempotency()
            .WithName("createSignup")
            .WithTags("Signup")
            .Accepts<SignupRequest>("application/json")
            .Produces<SignupCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/signups/{signupId:guid}", GetSignupAsync)
            .RequireAuthorization(PublicApiHosting.SignupPolicy)
            .WithName("getSignup")
            .WithTags("Signup")
            .Produces<SignupStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapPost("/signups/{signupId:guid}/resend-verification", ResendVerificationAsync)
            .RequireAuthorization(PublicApiHosting.SignupPolicy)
            .WithName("resendVerification")
            .WithTags("Signup")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/slugs/{slug}/availability", GetSlugAvailabilityAsync)
            .AllowAnonymous()
            .WithName("getSlugAvailability")
            .WithTags("Public")
            .Produces<SlugAvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// The plans on sale: public, offered plans with their active prices. Cached for 5 minutes, here
    /// and by the browser (<c>Cache-Control: public, max-age=300</c>).
    /// </summary>
    private static async Task<IResult> GetPlansAsync(HttpContext http, PlanService plans, IMemoryCache cache)
    {
        var list = await cache.GetOrCreateAsync(PlansCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlansCacheTime;
            return (await plans.ListOfferedAsync(http.RequestAborted))
                .Where(p => p.IsPublic)
                .OrderBy(p => p.Prices.Where(x => x.Status == PlanStatus.Active).Min(x => x.Amount))
                .Select(ToResponse)
                .ToList();
        });
        http.Response.Headers.CacheControl = $"public, max-age={(int)PlansCacheTime.TotalSeconds}";
        return Results.Ok(list);
    }

    public static PlanResponse ToResponse(Plan plan) => new(
        plan.Id,
        plan.Name,
        plan.Description,
        plan.TrialDays,
        (plan.Features ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        new PlanLimits(plan.MaxPeople),
        plan.Prices.Where(p => p.Status == PlanStatus.Active).OrderBy(p => p.Cycle).Select(ToResponse).ToList());

    public static PlanPriceResponse ToResponse(PlanPrice price) => new(price.Id, price.Cycle.ToString(), Money(price.Amount, price.Currency));

    public static MoneyResponse Money(decimal amount, string currency) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), currency);

    /// <summary>Is the organization address name free? A badly formed name is a 400; a taken or reserved one comes with a free suggestion.</summary>
    private static async Task<IResult> GetSlugAvailabilityAsync(HttpContext http, string slug, OrganizationService organizations)
    {
        var value = (slug ?? "").Trim().ToLowerInvariant();
        var problem = TenantProfile.SlugProblem(value);
        if (problem != null && !TenantProfile.Reserved.Contains(value))
            return Problems.Validation(http, new Dictionary<string, string[]> { ["slug"] = new[] { problem } });

        var check = await organizations.CheckSlugAsync(value, ct: http.RequestAborted);
        return Results.Ok(new SlugAvailabilityResponse(value, check.Available, check.Available ? null : check.Suggestion));
    }

    // ------------------------------------------------------------- registration

    private static string AppBase(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";

    private static async Task<IResult> CreateSignupAsync(HttpContext http, SignupRequest? body, Inprem.Platform.Application.PublicApi.SignupService signups)
    {
        if (body == null) return Problems.Validation(http, new Dictionary<string, string[]> { ["body"] = new[] { "A JSON body is required." } });
        var result = await signups.CreateAsync(new Inprem.Platform.Application.PublicApi.SignupInput(
            body.OrganizationName, body.Slug, body.TimeZone, body.Owner?.FirstName, body.Owner?.LastName, body.Owner?.Email, body.Owner?.Mobile,
            body.Consent?.AcceptTerms == true, body.Consent?.AcceptPrivacyPolicy == true, body.Consent?.SmsOptIn == true,
            body.PlanPriceId, body.Mode, body.CaptchaToken, http.Connection.RemoteIpAddress?.ToString()), AppBase(http), http.RequestAborted);

        if (result.Error is { } error)
        {
            return error switch
            {
                Inprem.Platform.Application.PublicApi.SignupError.Validation => Problems.Validation(http, result.Errors!),
                Inprem.Platform.Application.PublicApi.SignupError.Captcha => Problems.Create(http, StatusCodes.Status400BadRequest, Problems.CaptchaFailed, result.Message!),
                Inprem.Platform.Application.PublicApi.SignupError.PlanNotAvailable => Problems.Create(http, StatusCodes.Status400BadRequest, Problems.PlanNotAvailable, result.Message!),
                Inprem.Platform.Application.PublicApi.SignupError.SlugTaken => Problems.Create(http, StatusCodes.Status409Conflict, Problems.SlugTaken, result.Message!),
                Inprem.Platform.Application.PublicApi.SignupError.EmailInUse => Problems.Create(http, StatusCodes.Status409Conflict, Problems.EmailInUse, result.Message!),
                _ => Problems.Create(http, StatusCodes.Status500InternalServerError, Problems.InternalError, result.Message!),
            };
        }

        var signup = result.Signup!;
        return Results.Created($"{PublicApiHosting.Prefix}/signups/{signup.Id}", new SignupCreatedResponse(
            signup.Id, result.Token!, result.TokenExpiresUtc!.Value, "PendingVerification",
            signup.Mode == Inprem.Platform.Domain.PublicApi.SignupMode.Subscribe ? "checkout" : "verify_email"));
    }

    /// <summary>The signup token's own signup only; anything else is "not found".</summary>
    private static bool OwnSignup(HttpContext http, Guid signupId) => PublicApiClaims.SignupOf(http.User) == signupId;

    private static async Task<IResult> GetSignupAsync(HttpContext http, Guid signupId, Inprem.Platform.Application.PublicApi.SignupService signups)
    {
        var view = OwnSignup(http, signupId) ? await signups.GetAsync(signupId, http.RequestAborted) : null;
        return view == null
            ? Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No such signup.")
            : Results.Ok(new SignupStatusResponse(view.SignupId, view.Status, view.SubscriptionStatus?.ToString()));
    }

    private static async Task<IResult> ResendVerificationAsync(HttpContext http, Guid signupId, Inprem.Platform.Application.PublicApi.SignupService signups)
    {
        if (!OwnSignup(http, signupId)) return Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No such signup.");
        return await signups.ResendAsync(signupId, AppBase(http), http.RequestAborted) switch
        {
            Inprem.Platform.Application.PublicApi.ResendOutcome.Sent => Results.Accepted(),
            Inprem.Platform.Application.PublicApi.ResendOutcome.AlreadyVerified => Problems.Create(http, StatusCodes.Status409Conflict, Problems.AlreadyVerified, "The email is already verified."),
            Inprem.Platform.Application.PublicApi.ResendOutcome.LimitReached => Problems.Create(http, StatusCodes.Status409Conflict, Problems.ResendLimitReached, "At most 3 verification emails per hour. Please wait."),
            _ => Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No such signup."),
        };
    }

    public sealed record SignupOwner(string? FirstName, string? LastName, string? Email, string? Mobile);

    public sealed record SignupConsent(bool AcceptTerms, bool AcceptPrivacyPolicy, bool SmsOptIn);

    public sealed record SignupRequest(string? OrganizationName, string? Slug, string? TimeZone, SignupOwner? Owner, SignupConsent? Consent, Guid? PlanPriceId, string? Mode, string? CaptchaToken);

    public sealed record SignupCreatedResponse(Guid SignupId, string SignupToken, DateTime SignupTokenExpiresAt, string Status, string NextAction);

    public sealed record SignupStatusResponse(Guid SignupId, string Status, string? SubscriptionStatus);

    public sealed record HealthResponse(string Status);

    public sealed record MoneyResponse(string Amount, string Currency);

    /// <summary>
    /// Plan limits. <c>maxPeople</c> (staff and volunteers together) is what plans have today; the draft's
    /// separate staff, volunteer and admin limits wait for the plan catalogue decision (PO-5).
    /// </summary>
    public sealed record PlanLimits(int? MaxPeople);

    public sealed record PlanPriceResponse(Guid PlanPriceId, string BillingCycle, MoneyResponse Price);

    public sealed record PlanResponse(Guid PlanId, string Name, string? Description, int TrialDays, IReadOnlyList<string> Features, PlanLimits Limits, IReadOnlyList<PlanPriceResponse> Prices);

    public sealed record TimeZoneResponse(string Id, string DisplayName);

    public sealed record SlugAvailabilityResponse(string Slug, bool Available, string? Suggestion);
}
