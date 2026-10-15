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
