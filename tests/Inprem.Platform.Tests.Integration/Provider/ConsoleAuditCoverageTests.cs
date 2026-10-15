using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Phase 1 exit (P1-4): every console POST handler is listed here with the audit entry it writes
/// and the test that checks it. A new handler fails this test until it is added, with its audit
/// test, so no console change can go unaudited unnoticed.
/// </summary>
public class ConsoleAuditCoverageTests
{
    /// <summary>"Page.Handler" (relative to Areas.Provider.Pages) → audit action, and where it is tested.</summary>
    private static readonly Dictionary<string, (string Audit, string Test)> Reviewed = new()
    {
        // Sign-in pages: no console change. Failed attempts are counted (SecurityAttempts), not audited.
        ["Login.OnPostAsync"] = ("none (sign-in)", "SetupJourneyTests"),
        ["LoginWith2fa.OnPostAsync"] = ("none (sign-in)", "SetupJourneyTests"),
        ["LoginWithRecoveryCode.OnPostAsync"] = ("provider.recovery.level1", "SetupJourneyTests"),
        ["Logout.OnPostAsync"] = ("none (sign-out)", "ConsoleAccessTests"),
        ["Reauthenticate.OnPostAsync"] = ("none (fresh two-factor code)", "ConsoleAccessTests"),

        // Accounts and recovery.
        ["Setup.OnPostAsync"] = ("provider.bootstrap.succeeded / .refused", "SetupJourneyTests"),
        ["AcceptInvitation.OnPostAsync"] = ("provider.invitation.accepted", "ProviderUserPagesTests"),
        ["EnrolTwoFactor.OnPostAsync"] = ("provider.two-factor.enrolled", "ProviderUserPagesTests"),
        ["ApproveRecovery.OnPostAsync"] = ("provider.recovery.level3/4.approved", "ServerCommandTests"),

        // Team.
        ["Users.InviteModel.OnPostAsync"] = ("provider.user.invited", "ProviderUserPagesTests"),
        ["Users.RoleModel.OnPostAsync"] = ("provider.user.role-changed", "ProviderUserPagesTests"),
        ["Users.DisableModel.OnPostAsync"] = ("provider.user.disabled", "ProviderUserPagesTests"),
        ["Users.ResetTwoFactorModel.OnPostAsync"] = ("provider.recovery.level2", "ProviderUserPagesTests"),
        ["Users.DetailsModel.OnPostResendAsync"] = ("provider.user.invitation-resent", "ProviderUserPagesTests"),

        // Plans.
        ["Plans.NewModel.OnPostAsync"] = ("plan.created", "PlanPagesTests"),
        ["Plans.EditModel.OnPostAsync"] = ("plan.updated", "PlanPagesTests"),
        ["Plans.NewPriceModel.OnPostAsync"] = ("plan.price-added", "PlanPagesTests"),
        ["Plans.DetailsModel.OnPostRetireAsync"] = ("plan.retired", "PlanPagesTests"),
        ["Plans.DetailsModel.OnPostReactivateAsync"] = ("plan.reactivated", "PlanPagesTests"),
        ["Plans.DetailsModel.OnPostRetirePriceAsync"] = ("plan.price-retired", "PlanPagesTests"),
        ["Plans.DetailsModel.OnPostPublishAsync"] = ("plan.published / plan.publish-failed", "PlanPublishTests"),

        // Organizations.
        ["Tenants.NewModel.OnPostAsync"] = ("organization.created", "OrganizationPagesTests"),
        ["Tenants.EditModel.OnPostAsync"] = ("organization.updated", "OrganizationPagesTests"),
        ["Tenants.SuspendModel.OnPostAsync"] = ("organization.suspended", "OrganizationPagesTests"),
        ["Tenants.ReactivateModel.OnPostAsync"] = ("organization.reactivated", "OrganizationPagesTests"),
        ["Tenants.AddNoteModel.OnPostAsync"] = ("organization.note-added", "OrganizationPagesTests"),
        ["Tenants.TransferOwnershipModel.OnPostAsync"] = ("organization.ownership-transferred", "OrganizationPagesTests"),
        ["Tenants.DetailsModel.OnPostResendAsync"] = ("organization.invitation-resent", "OrganizationPagesTests"),
        ["Tenants.DetailsModel.OnPostResetAsync"] = ("organization.password-reset-sent", "OrganizationPagesTests"),
        ["Tenants.DetailsModel.OnPostEndSupportAsync"] = ("support.ended", "SupportSessionTests"),
        ["Tenants.StartSupportModel.OnPostAsync"] = ("support.started", "SupportSessionTests"),
        ["Tenants.DetailsModel.OnPostSyncAsync"] = ("subscription.synced / the correction", "BillingPageTests"),
        ["Tenants.ExtendTrialModel.OnPostAsync"] = ("subscription.trial-extended", "BillingPageTests"),
        ["Tenants.FreeAccessModel.OnPostAsync"] = ("subscription.free-access-granted / -removed", "BillingPageTests"),
        ["Tenants.CancelSubscriptionModel.OnPostAsync"] = ("subscription.canceled", "BillingPageTests"),

        // Webhook events.
        ["Webhooks.DetailsModel.OnPostReplayAsync"] = ("billing-event.replayed", "WebhookTests"),
    };

    private const string Root = "InpremClockingApp.Areas.Provider.Pages.";

    private static IEnumerable<string> PostHandlers() =>
        from type in typeof(InpremClockingApp.Provider.ProviderPolicies).Assembly.GetTypes()
        where typeof(PageModel).IsAssignableFrom(type) && type.FullName!.StartsWith(Root, StringComparison.Ordinal)
        from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        where method.Name.StartsWith("OnPost", StringComparison.Ordinal)
        select Name(type) + "." + method.Name;

    // Top-level pages as "Login", pages in folders as "Users.InviteModel" (several folders share class names).
    private static string Name(Type type)
    {
        var relative = type.FullName![Root.Length..];
        return relative.Contains('.') ? relative : relative[..^"Model".Length];
    }

    [Fact]
    public void Console_Every_Post_Handler_Is_Reviewed_For_Its_Audit_Entry()
    {
        var handlers = PostHandlers().ToHashSet();
        Assert.Empty(handlers.Except(Reviewed.Keys).Order()); // new handler: add it here with its audit test
        Assert.Empty(Reviewed.Keys.Except(handlers).Order()); // stale entry: remove it
    }
}
