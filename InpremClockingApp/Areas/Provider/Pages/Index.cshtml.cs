using Inprem.Platform.Application.Provider;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>Provider Console home (full console access). The console screens arrive in Phase 1.</summary>
public class IndexModel : PageModel
{
    private readonly ProviderSecurityService _provider;

    public IndexModel(ProviderSecurityService provider) => _provider = provider;

    public OwnerStatus Owners { get; private set; } = new(0, 0);

    public async Task OnGetAsync() => Owners = await _provider.GetOwnerStatusAsync(HttpContext.RequestAborted);
}
