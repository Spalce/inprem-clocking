using System.Net;
using System.Text;
using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using PlatformEmailSender = Inprem.Platform.Application.Memberships.IEmailSender;

namespace InpremClockingApp.Provider.Organizations;

/// <summary>
/// Emails to organization admins, with links to the App site (Links:AppBaseUrl, or the request's
/// own address on a combined site). The "set your password" link is Identity's password-reset
/// link to the approved reset page: valid for one day (Identity's default token lifetime).
/// </summary>
public sealed class OrganizationMail : IOrganizationMail
{
    private readonly UserManager<AppUser> _users;
    private readonly PlatformEmailSender _email;
    private readonly IConfiguration _configuration;

    public OrganizationMail(UserManager<AppUser> users, PlatformEmailSender email, IConfiguration configuration)
    {
        _users = users;
        _email = email;
        _configuration = configuration;
    }

    public bool CanBuildLinks(string? appLinkBase) => Base(appLinkBase) != null;

    public async Task SendOwnerWelcomeAsync(string userId, string organizationName, string? appLinkBase, CancellationToken ct = default)
    {
        var (user, link) = await ResetLinkAsync(userId, appLinkBase);
        await _email.SendAsync(user.Email!, $"You are the owner of {organizationName} on Inprem Clock-in", Html(
            $"An account was set up for you as the owner of {organizationName} on the Inprem Clock-in app.",
            "Choose your password with this link (it works for 24 hours):", link,
            $"Then sign in with {user.Email} at:", $"{Base(appLinkBase)}/Identity/Account/Login"), ct);
    }

    public async Task SendOwnerInvitationAsync(string userId, string organizationName, string? appLinkBase, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId) ?? throw new InvalidOperationException("The account does not exist.");
        await _email.SendAsync(user.Email!, $"You have been invited to {organizationName} on Inprem Clock-in", Html(
            $"You have been added as the owner of {organizationName} on the Inprem Clock-in app.",
            "Sign in with your existing account and accept the invitation here:", $"{Base(appLinkBase)}/Account/Invitations"), ct);
    }

    public async Task SendPasswordResetAsync(string userId, string organizationName, string? appLinkBase, CancellationToken ct = default)
    {
        var (user, link) = await ResetLinkAsync(userId, appLinkBase);
        await _email.SendAsync(user.Email!, "Reset your Inprem Clock-in password", Html(
            $"The Inprem support team sent you a link to reset your password for {organizationName}.",
            "Choose a new password with this link (it works for 24 hours):", link,
            "If you did not ask for this, you can ignore this email."), ct);
    }

    private async Task<(AppUser User, string Link)> ResetLinkAsync(string userId, string? appLinkBase)
    {
        var user = await _users.FindByIdAsync(userId) ?? throw new InvalidOperationException("The account does not exist.");
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return (user, $"{Base(appLinkBase)}/Identity/Account/ResetPassword?code={code}");
    }

    private string? Base(string? appLinkBase)
    {
        var configured = _configuration["Links:AppBaseUrl"];
        var value = !string.IsNullOrWhiteSpace(configured) ? configured : appLinkBase;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
    }

    private static string Html(params string[] paragraphs) =>
        string.Concat(paragraphs.Select(p => p.StartsWith("http", StringComparison.Ordinal)
            ? $"<p><a href=\"{WebUtility.HtmlEncode(p)}\">{WebUtility.HtmlEncode(p)}</a></p>"
            : $"<p>{WebUtility.HtmlEncode(p)}</p>"));
}
