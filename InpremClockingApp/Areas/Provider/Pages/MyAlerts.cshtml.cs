using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// "My alerts" (Phase 5 step 5.3, design Appendix C): which Inprem alerts this console user gets, by
/// email, and by text on their confirmed phone if they agree. Which alerts a role may get is fixed
/// (billing problems: Owners and Billing; system problems: Owners); the page shows only those.
/// </summary>
[Authorize(Policy = ProviderPolicies.Support)]
public class MyAlertsModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly ProviderAlertSettings _settings;
    private readonly IAuthorizationService _authorization;

    public MyAlertsModel(UserManager<AppUser> users, ProviderAlertSettings settings, IAuthorizationService authorization)
    {
        _users = users;
        _settings = settings;
        _authorization = authorization;
    }

    [BindProperty] public bool NewSignups { get; set; }
    [BindProperty] public AlertDelivery BillingProblems { get; set; }
    [BindProperty] public bool SystemProblems { get; set; }
    [BindProperty] public bool Texts { get; set; }

    public bool MayGetBilling { get; private set; }
    public bool MayGetSystem { get; private set; }
    public string? Phone { get; private set; }
    public string Wording => ConsentWording.ProviderAlerts;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        await LoadAsync(user);
        var current = await _settings.GetAsync(user.Id, ConfirmedPhone(user), HttpContext.RequestAborted);
        (NewSignups, BillingProblems, SystemProblems, Texts) = (current.NewSignups, current.BillingProblems, current.SystemProblems, current.Texts);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        await LoadAsync(user);
        var current = await _settings.GetAsync(user.Id, ConfirmedPhone(user), HttpContext.RequestAborted);
        // Choices the role does not offer keep their value (they apply again if the role changes).
        var choices = new ProviderAlertChoices(NewSignups, MayGetBilling ? BillingProblems : current.BillingProblems, MayGetSystem ? SystemProblems : current.SystemProblems, Texts);
        if (!await _settings.SaveAsync(user.Id, ConfirmedPhone(user), choices, HttpContext.RequestAborted))
        {
            Error = "To get alerts by text, first confirm your phone on \"My phone for codes\".";
            return Page();
        }
        TempData["Message"] = "Your alert choices are saved.";
        return RedirectToPage();
    }

    private async Task LoadAsync(AppUser user)
    {
        MayGetBilling = (await _authorization.AuthorizeAsync(User, ProviderPolicies.Billing)).Succeeded;
        MayGetSystem = (await _authorization.AuthorizeAsync(User, ProviderPolicies.Owner)).Succeeded;
        Phone = ConfirmedPhone(user) is { } phone ? PhoneNumber.Mask(PhoneNumber.ToE164(phone)) : null;
    }

    private static string? ConfirmedPhone(AppUser user) => user.PhoneNumberConfirmed ? user.PhoneNumber : null;
}
