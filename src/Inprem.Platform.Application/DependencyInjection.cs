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
        services.AddScoped<Billing.PlanService>();
        services.AddScoped<Billing.PlanPublisher>();
        services.AddScoped<Billing.CheckoutService>();
        services.AddScoped<Billing.SubscriptionSync>();
        services.AddScoped<Billing.BillingWebhookService>();
        services.AddScoped<Billing.BillingEventProcessor>();
        services.AddScoped<Billing.BillingReconciliation>();
        services.AddScoped<Billing.IEntitlementService, Billing.EntitlementService>();
        services.AddScoped<Billing.BillingAccountService>();
        services.AddScoped<PublicApi.PublicApiTokens>();
        services.AddScoped<PublicApi.SignupService>();
        services.AddScoped<PublicApi.BillingAccessService>();
        services.AddScoped<Organizations.OrganizationService>();
        services.AddScoped<Support.SupportSessionService>();
        services.AddScoped<Audit.AuditLogService>();
        return services;
    }
}
