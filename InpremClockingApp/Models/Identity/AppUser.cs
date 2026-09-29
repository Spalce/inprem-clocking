using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Models.Identity;

public class AppUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Type { get; set; }

    // Nullable for now (multi-tenancy Phase 0) - backfilled and made required for ordinary
    // tenant users in Phase 1. Stays null for SuperAdmin accounts, which aren't scoped to any
    // one tenant. Stamped into a claim at sign-in (Phase 2) - see multi-tenancy.md.
    public int? TenantId { get; set; }
}
