using Inprem.Platform.Application.Provider;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// Approver's confirmation of a Level 3 or 4 recovery, from the emailed link (valid 1 hour). The
/// link only shows the request; approving takes a button press (POST), so link previews and
/// mail scanners cannot approve by opening it.
/// </summary>
[ProviderAccess(ProviderAccessLevel.Anonymous)]
public class ApproveRecoveryModel : PageModel
{
    private readonly ProviderSecurityService _provider;

    public ApproveRecoveryModel(ProviderSecurityService provider) => _provider = provider;

    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    [BindProperty] public bool WaiveDelay { get; set; }

    public RecoveryApprovalInfo? Recovery { get; private set; }
    public ProviderResult? Result { get; private set; }

    public async Task OnGetAsync() => Recovery = await _provider.FindRecoveryForApprovalAsync(Token, HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostAsync()
    {
        Result = await _provider.ApproveRecoveryAsync(Token, WaiveDelay, HttpContext.RequestAborted);
        Recovery = await _provider.FindRecoveryForApprovalAsync(Token, HttpContext.RequestAborted);
        return Page();
    }
}
