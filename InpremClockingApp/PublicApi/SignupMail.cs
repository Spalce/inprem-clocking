using System.Net;
using System.Text;
using Inprem.Platform.Application.PublicApi;
using Inprem.Platform.Domain.PublicApi;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using PlatformEmailSender = Inprem.Platform.Application.Memberships.IEmailSender;

namespace InpremClockingApp.PublicApi;

/// <summary>
/// The verification email of a website signup: Identity's email-confirmation link to the app's
/// <c>/Account/VerifyEmail</c> page, where the owner also chooses a password (valid 24 hours).
/// </summary>
public sealed class SignupMail : ISignupMail
{
    private readonly UserManager<AppUser> _users;
    private readonly PlatformEmailSender _email;
    private readonly IConfiguration _configuration;

    public SignupMail(UserManager<AppUser> users, PlatformEmailSender email, IConfiguration configuration)
    {
        _users = users;
        _email = email;
        _configuration = configuration;
    }

    public bool CanBuildLinks(string? appLinkBase) => Base(appLinkBase) != null;

    public async Task SendVerificationAsync(Signup signup, string organizationName, string? appLinkBase, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(signup.OwnerUserId) ?? throw new InvalidOperationException("The owner's account does not exist.");
        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{Base(appLinkBase)}/Account/VerifyEmail?signup={signup.Id}&code={WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))}";
        await _email.SendAsync(user.Email!, $"Confirm your email for {organizationName} on Inprem Clock-in", string.Concat(new[]
        {
            $"Thank you for registering {organizationName} on Inprem Clock-in.",
            "Confirm your email address and choose your password with this link (it works for 24 hours):",
            link,
            "If you did not register, you can ignore this email: nothing happens without the confirmation.",
        }.Select(p => p.StartsWith("http", StringComparison.Ordinal)
            ? $"<p><a href=\"{WebUtility.HtmlEncode(p)}\">{WebUtility.HtmlEncode(p)}</a></p>"
            : $"<p>{WebUtility.HtmlEncode(p)}</p>")), ct);
    }

    private string? Base(string? appLinkBase)
    {
        var configured = _configuration["Links:AppBaseUrl"];
        var value = !string.IsNullOrWhiteSpace(configured) ? configured : appLinkBase;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
    }
}
