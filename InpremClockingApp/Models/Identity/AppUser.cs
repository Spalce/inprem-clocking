using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Models.Identity;

public class AppUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Type { get; set; }

    // Stays nullable at the DB level - every existing user has been backfilled to a real
    // tenant, but this column is intentionally left nullable to support future SuperAdmin
    // accounts, which aren't scoped to any one tenant. Stamped into a claim at sign-in
    // (Phase 2) - see multi-tenancy.md.
    public int? TenantId { get; set; }
}
