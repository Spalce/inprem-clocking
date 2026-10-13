using Inprem.Platform.Application.Provider;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// One-time setup page (D13) for hosts without a console: exactly what <c>provider bootstrap</c>
/// does. Exists only while no Provider Owner exists and Bootstrap:CodeHash is configured;
/// otherwise 404. Rate-limited and audited by the shared use case.
/// </summary>
[ProviderAccess(ProviderAccessLevel.Anonymous)]
public class SetupModel : PageModel
{
    private readonly ProviderSecurityService _provider;

    public SetupModel(ProviderSecurityService provider) => _provider = provider;

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Code { get; set; } = "";

    public string? Error { get; private set; }
    public string? Done { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await _provider.IsSetupAvailableAsync(HttpContext.RequestAborted) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await _provider.IsSetupAvailableAsync(HttpContext.RequestAborted)) return NotFound();

        // Used only when Links:AdminBaseUrl is not configured: the console base as this request sees it.
        var acceptUrl = Url.Page("/AcceptInvitation", null, new { area = "Provider" }, Request.Scheme)!;
        var linkBase = acceptUrl[..acceptUrl.LastIndexOf('/')];
        var actor = $"anonymous:{HttpContext.Connection.RemoteIpAddress}";

        var result = await _provider.BootstrapAsync(new BootstrapRequest(Email, Code, actor, DryRun: false, linkBase), HttpContext.RequestAborted);
        Code = "";
        if (result.Succeeded) Done = result.Message;
        else Error = result.Message;
        return Page();
    }
}
