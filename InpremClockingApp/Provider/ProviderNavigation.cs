namespace InpremClockingApp.Provider;

/// <summary>One item of the Provider Console menu.</summary>
/// <param name="Page">Razor page in the Provider area (null for <see cref="Href"/> links).</param>
/// <param name="Href">A fixed link outside the pages (the jobs dashboard).</param>
public sealed record ProviderNavItem(string Title, string Icon, string Policy, string? Page, string? Href = null);

/// <summary>The console menu (step 1.1), in order. Each item is shown only to roles its policy allows.</summary>
public static class ProviderNavigation
{
    public static readonly IReadOnlyList<ProviderNavItem> Items = new[]
    {
        new ProviderNavItem("Dashboard", "fa-gauge", ProviderPolicies.Support, "/Index"),
        new ProviderNavItem("Organizations", "fa-building", ProviderPolicies.Support, "/Tenants/Index"),
        new ProviderNavItem("Plans", "fa-tags", ProviderPolicies.Support, "/Plans/Index"),
        new ProviderNavItem("Billing", "fa-file-invoice-dollar", ProviderPolicies.Billing, "/Billing/Index"),
        new ProviderNavItem("Webhook events", "fa-plug", ProviderPolicies.Billing, "/Webhooks/Index"),
        new ProviderNavItem("Notifications", "fa-bell", ProviderPolicies.Owner, "/Notifications/Index"),
        new ProviderNavItem("Jobs", "fa-gears", ProviderPolicies.Owner, null, Href: "jobs"),
        new ProviderNavItem("Audit log", "fa-clipboard-list", ProviderPolicies.Owner, "/Audit/Index"),
        new ProviderNavItem("Provider users", "fa-user-shield", ProviderPolicies.Owner, "/Users/Index"),
        new ProviderNavItem("Settings", "fa-sliders", ProviderPolicies.Owner, "/Settings/Index"),
    };
}
