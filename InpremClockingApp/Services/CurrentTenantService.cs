using InpremClockingApp.Helpers;
using Microsoft.AspNetCore.Http;

namespace InpremClockingApp.Services;

// The single seam between "how a request's tenant is determined" and everything else in the
// app - every EF Core query filter and every new-row TenantId stamp (ApplicationDbContext)
// reads TenantId from here. Swapping the resolution mechanism later (e.g. to subdomain-based,
// see multi-tenancy.md decision #5) means changing only this class.
public interface ICurrentTenantService
{
    int? TenantId { get; }
}

public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    // Null before sign-in (login lookups must see every tenant's users - see the AppUser
    // query filter in ApplicationDbContext) and for a signed-in user with no tenant claim
    // (reserved for a future tenant-less SuperAdmin role).
    public int? TenantId
    {
        get
        {
            var claimValue = _httpContextAccessor.HttpContext?.User?.FindFirst(TenantClaimTypes.TenantId)?.Value;
            return int.TryParse(claimValue, out var tenantId) ? tenantId : null;
        }
    }
}
