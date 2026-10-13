using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Inprem.Platform.Application;

public static class DependencyInjection
{
    /// <summary>Registers the platform's use cases and the per-request tenant context.</summary>
    public static IServiceCollection AddPlatformApplication(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IMembershipService, MembershipService>();
        return services;
    }
}
