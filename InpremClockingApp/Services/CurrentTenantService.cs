using Inprem.Platform.Application.Tenancy;

namespace InpremClockingApp.Services;

// The single seam between "how a request's tenant is determined" and everything else in the
// app - every EF Core query filter and every new-row TenantId stamp (ApplicationDbContext)
// reads TenantId from here. Swapping the resolution mechanism later (e.g. to subdomain-based,
// see multi-tenancy.md decision #5) means changing only this class.
public interface ICurrentTenantService
{
    int? TenantId { get; }
}

// C2-8 (multi-tenancy Phase 0 step 0.3): reads the tenant from the validated ITenantContext,
// which TenantContextMiddleware fills only after re-checking the signed-in membership on this
// request - no longer straight from the cookie's tenant_id claim. Null before sign-in (login
// lookups must see every tenant's users - see the AppUser query filter in ApplicationDbContext),
// outside requests (IdentitySeeder at startup), and for platform operators.
public class CurrentTenantService : ICurrentTenantService
{
    private readonly ITenantContext _tenantContext;

    public CurrentTenantService(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public int? TenantId => _tenantContext.TenantId;
}
