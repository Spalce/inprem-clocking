using System.ComponentModel.DataAnnotations;
using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages.Platform;

// SuperAdmin-only (see Program.cs AuthorizePage("/Platform/Tenants", "SuperAdminOnly") and
// multi-tenancy.md Phase 4) - the only page in the app that creates a Tenant row.
public class Tenants : PageModel
{
    private readonly TenantAdminService _service;

    public Tenants(TenantAdminService service)
    {
        _service = service;
    }

    public List<TenantListItem> AllTenants { get; set; } = new();

    [BindProperty]
    public CreateTenantInput Input { get; set; } = new();

    public class CreateTenantInput
    {
        [Required, StringLength(200)]
        [Display(Name = "Organization Name")]
        public string TenantName { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Display(Name = "Timezone (IANA id, e.g. America/New_York)")]
        public string TimeZoneId { get; set; } = "America/New_York";

        [Required, EmailAddress]
        [Display(Name = "First Admin Email")]
        public string AdminEmail { get; set; } = string.Empty;

        [Required]
        [Display(Name = "First Admin First Name")]
        public string AdminFirstName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "First Admin Last Name")]
        public string AdminLastName { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 6)]
        [Display(Name = "First Admin Password")]
        public string AdminPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        AllTenants = await _service.GetAllTenantsWithSubscriptionsAsync().ConfigureAwait(true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(Input.TimeZoneId, out _))
        {
            ModelState.AddModelError(nameof(Input.TimeZoneId), "Not a recognized timezone id (e.g. America/New_York, America/Chicago, America/Los_Angeles).");
        }

        if (!ModelState.IsValid)
        {
            AllTenants = await _service.GetAllTenantsWithSubscriptionsAsync().ConfigureAwait(true);
            return Page();
        }

        var result = await _service.CreateTenantWithAdminAsync(
            Input.TenantName, Input.TimeZoneId, Input.AdminEmail, Input.AdminFirstName, Input.AdminLastName, Input.AdminPassword)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            AllTenants = await _service.GetAllTenantsWithSubscriptionsAsync().ConfigureAwait(true);
            return Page();
        }

        TempData["Message"] = $"Tenant \"{Input.TenantName}\" created with admin {Input.AdminEmail}.";
        return RedirectToPage("./Tenants");
    }
}
