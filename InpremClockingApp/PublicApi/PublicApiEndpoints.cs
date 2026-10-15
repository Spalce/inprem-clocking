namespace InpremClockingApp.PublicApi;

/// <summary>The website API's endpoints (Phase 3). Step 3.1: the skeleton and <c>GET /health</c>.</summary>
public static class PublicApiEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
            .AllowAnonymous()
            .WithName("getHealth")
            .WithTags("Public")
            .Produces<HealthResponse>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    public sealed record HealthResponse(string Status);
}
