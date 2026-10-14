using Inprem.Platform.Application.Provider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Users;

/// <summary>Pages about one provider user (Owners only, by the area convention plus [Authorize] on each page).</summary>
public abstract class ProviderUserPage : PageModel
{
    protected ProviderUserPage(ProviderSecurityService provider) => Provider = provider;

    protected ProviderSecurityService Provider { get; }

    public ProviderUserRow Person { get; private set; } = null!;
    public string? Error { get; protected set; }

    protected async Task<bool> LoadAsync(string userId)
    {
        var person = (await Provider.ListProviderUsersAsync(HttpContext.RequestAborted)).FirstOrDefault(u => u.UserId == userId);
        if (person == null) return false;
        Person = person;
        return true;
    }

    /// <summary>The console's base address for setup links when Links:AdminBaseUrl is not configured.</summary>
    protected string ConsoleLinkBase()
    {
        var accept = Url.Page("/AcceptInvitation", null, new { area = "Provider" }, Request.Scheme)!;
        return accept[..accept.LastIndexOf('/')];
    }
}
