using Inprem.Platform.Application.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing;

/// <summary>The monthly reconciliation report (Phase 4 step 4.3, P4-3): console against PayPal, with a CSV. Read only. Owners and Billing.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class ReconciliationModel : PageModel
{
    private readonly MonthlyBillingReport _reports;

    public ReconciliationModel(MonthlyBillingReport reports) => _reports = reports;

    /// <summary>The month as yyyy-MM; the previous month by default.</summary>
    [BindProperty(SupportsGet = true)] public string? Month { get; set; }

    public MonthlyReport Report { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!TryMonth(out var year, out var month)) return BadRequest();
        Report = await _reports.BuildAsync(year, month, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        if (!TryMonth(out var year, out var month)) return BadRequest();
        var report = await _reports.BuildAsync(year, month, HttpContext.RequestAborted);
        return File(MonthlyBillingReport.Csv(report), "text/csv", $"inprem-paypal-reconciliation-{year:D4}-{month:D2}.csv");
    }

    private bool TryMonth(out int year, out int month)
    {
        var previous = DateTime.UtcNow.AddMonths(-1);
        (year, month) = (previous.Year, previous.Month);
        if (string.IsNullOrWhiteSpace(Month))
        {
            Month = $"{year:D4}-{month:D2}";
            return true;
        }
        if (!DateTime.TryParseExact(Month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var chosen)
            || chosen.Year < 2020 || chosen > DateTime.UtcNow) return false;
        (year, month) = (chosen.Year, chosen.Month);
        return true;
    }
}
