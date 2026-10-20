using System.Globalization;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Tenancy;
using InpremClockingApp.Areas.Billing.Pages;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Areas.Settings.Pages;

/// <summary>
/// Settings → Staff alerts (Phase 6 step 6.1, C3-8, off by default): the organization's Owner switches
/// on texts to staff and volunteers who agreed to them, and sets the missed clock-out time (local),
/// the long-break limit and the end-of-day summary to admins. Owners only, like Billing; never in a
/// support session.
/// </summary>
public class StaffAlertsModel : OwnerPageModel
{
    private readonly StaffAlertSettings _settings;

    public StaffAlertsModel(ITenantContext tenant, StaffAlertSettings settings) : base(tenant) => _settings = settings;

    [BindProperty] public bool Enabled { get; set; }

    /// <summary>"HH:mm", from an input of type time.</summary>
    [BindProperty] public string MissedClockOut { get; set; } = "21:00";
    [BindProperty] public int LongBreakMinutes { get; set; }
    [BindProperty] public bool AdminSummary { get; set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        var current = await _settings.GetAsync(TenantId, HttpContext.RequestAborted);
        (Enabled, MissedClockOut, LongBreakMinutes, AdminSummary) = (current.Enabled, current.MissedClockOut.ToString("HH:mm", CultureInfo.InvariantCulture), current.LongBreakMinutes, current.AdminSummary);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!TimeOnly.TryParseExact(MissedClockOut ?? "", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            Error = "Enter the missed clock-out time as hours and minutes, e.g. 21:00.";
            return Page();
        }
        Error = await _settings.SaveAsync(TenantId, new StaffAlertChoices(Enabled, time, LongBreakMinutes, AdminSummary), Actor, HttpContext.RequestAborted);
        if (Error != null) return Page();
        TempData["Message"] = Enabled
            ? "Staff alerts are on. The staff and volunteer forms now show the mobile number and the texts box."
            : "Staff alerts are off. The forms are as before.";
        return RedirectToPage();
    }
}
