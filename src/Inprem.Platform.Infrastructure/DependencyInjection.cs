using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Inprem.Platform.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the platform's infrastructure: PlatformDbContext and the stores behind the Application interfaces.</summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PlatformDbContext>(options => PlatformDbContext.Configure(options, connectionString));
        services.AddScoped<IMembershipStore, MembershipStore>();
        services.AddScoped<Application.Provider.IProviderStore, ProviderStore>();
        services.AddScoped<Application.Billing.IPlanStore, PlanStore>();
        services.AddScoped<Application.Support.ISupportSessionStore, SupportSessionStore>();
        services.AddScoped<Application.Audit.IAuditReader, AuditReader>();
        services.AddScoped<Application.Billing.ICheckoutStore, CheckoutStore>();
        services.AddScoped<Application.Billing.IBillingEventStore, BillingEventStore>();
        services.AddScoped<Application.Billing.IReconciliationStore, ReconciliationStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddMemoryCache();
        return services;
    }
}
