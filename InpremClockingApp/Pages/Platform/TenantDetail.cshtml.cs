using System.ComponentModel.DataAnnotations;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Billing;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages.Platform;

// SuperAdmin-only (see Program.cs's AuthorizeFolder("/Platform", "SuperAdminOnly") and
// multi-tenancy.md Part 2, Phase 7). Edit surface for a single tenant's profile, active
// status, and subscription - everything /Platform/Tenants doesn't cover after creation.
public class TenantDetail : PageModel
{
    private readonly TenantAdminService _service;

    public TenantDetail(TenantAdminService service)
    {
        _service = service;
    }

    public int Id { get; set; }

    public Tenant? Tenant { get; set; }

    public List<AppUser> Admins { get; set; } = new();

    public Subscription? Subscription { get; set; }

    [BindProperty]
    public TenantDetailsInput DetailsInput { get; set; } = new();

    [BindProperty]
    public SubscriptionInput SubInput { get; set; } = new();

    public class TenantDetailsInput
    {
        [Required, StringLength(200)]
        [Display(Name = "Organization Name")]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Display(Name = "Timezone (IANA id, e.g. America/New_York)")]
        public string TimeZoneId { get; set; } = string.Empty;

        [StringLength(255)]
        public string? Address { get; set; }

        [StringLength(255)]
        [Display(Name = "Contact Info")]
        public string? ContactInfo { get; set; }
    }

    public class SubscriptionInput
    {
        [Range(0, 1000000)]
        public decimal Amount { get; set; }

        public BillingCycle BillingCycle { get; set; }

        public SubscriptionStatus Status { get; set; }

        [Required, DataType(DataType.Date)]
        [Display(Name = "Current Period Start")]
        public DateTime CurrentPeriodStart { get; set; }

        [Required, DataType(DataType.Date)]
        [Display(Name = "Current Period End")]
        public DateTime CurrentPeriodEnd { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id).ConfigureAwait(true)) return NotFound();

        DetailsInput = new TenantDetailsInput
        {
            Name = Tenant!.Name,
            TimeZoneId = Tenant.TimeZoneId,
            Address = Tenant.Address,
            ContactInfo = Tenant.ContactInfo,
        };

        if (Subscription != null)
        {
            SubInput = new SubscriptionInput
            {
                Amount = Subscription.Amount,
                BillingCycle = Subscription.BillingCycle,
                Status = Subscription.Status,
                CurrentPeriodStart = Subscription.CurrentPeriodStart,
                CurrentPeriodEnd = Subscription.CurrentPeriodEnd,
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateDetailsAsync(int id)
    {
        if (!await LoadAsync(id).ConfigureAwait(true)) return NotFound();

        ModelState.Clear();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(DetailsInput.TimeZoneId, out _))
        {
            ModelState.AddModelError("DetailsInput.TimeZoneId", "Not a recognized timezone id (e.g. America/New_York, America/Chicago, America/Los_Angeles).");
        }
        if (!TryValidateModel(DetailsInput, nameof(DetailsInput)) || !ModelState.IsValid)
        {
            return Page();
        }

        await _service.UpdateTenantDetailsAsync(id, DetailsInput.Name, DetailsInput.TimeZoneId, DetailsInput.Address, DetailsInput.ContactInfo)
            .ConfigureAwait(true);

        TempData["Message"] = $"Updated details for \"{DetailsInput.Name}\".";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUpdateSubscriptionAsync(int id)
    {
        if (!await LoadAsync(id).ConfigureAwait(true)) return NotFound();

        ModelState.Clear();
        if (SubInput.CurrentPeriodEnd <= SubInput.CurrentPeriodStart)
        {
            ModelState.AddModelError("SubInput.CurrentPeriodEnd", "Period end must be after period start.");
        }
        if (!TryValidateModel(SubInput, nameof(SubInput)) || !ModelState.IsValid)
        {
            return Page();
        }

        await _service.UpdateSubscriptionAsync(
            id, SubInput.Amount, SubInput.BillingCycle, SubInput.Status, SubInput.CurrentPeriodStart, SubInput.CurrentPeriodEnd)
            .ConfigureAwait(true);

        TempData["Message"] = "Subscription updated.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var tenant = await _service.GetTenantByIdAsync(id).ConfigureAwait(true);
        if (tenant == null) return NotFound();

        var newStatus = !tenant.IsActive;
        await _service.SetTenantActiveAsync(id, newStatus).ConfigureAwait(true);

        TempData["Message"] = $"\"{tenant.Name}\" is now {(newStatus ? "active" : "suspended")}.";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        Id = id;
        Tenant = await _service.GetTenantByIdAsync(id).ConfigureAwait(true);
        if (Tenant == null) return false;

        Admins = await _service.GetAdminsForTenantAsync(id).ConfigureAwait(true);
        Subscription = await _service.GetSubscriptionForTenantAsync(id).ConfigureAwait(true);
        return true;
    }
}
