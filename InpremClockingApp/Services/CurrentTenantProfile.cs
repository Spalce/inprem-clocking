using Inprem.Platform.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using InpremClockingApp.Data;
using InpremClockingApp.Models;

namespace InpremClockingApp.Services;

/// <summary>
/// Exposes the current tenant's display profile (name/address/contact info) for report and PDF
/// headers - added in the Phase 5 hardening pass (multi-tenancy.md) after those were found
/// hardcoded to Inprem's own details everywhere, which leaked into every other tenant's
/// official documents. Resolved lazily and cached per request/scope, same pattern as
/// TenantClock - reuses the Tenant query filter, so it naturally resolves to the signed-in
/// user's own tenant.
/// </summary>
public interface ICurrentTenantProfile
{
    string Name { get; }
    string? Address { get; }
    string? ContactInfo { get; }
}

public class CurrentTenantProfile : ICurrentTenantProfile
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private Tenant? _tenant;
    private bool _loaded;

    public CurrentTenantProfile(ApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    // C2-9 (multi-tenancy Phase 0 step 0.3): looked up by the validated ITenantContext's id.
    private Tenant? Resolve()
    {
        if (!_loaded)
        {
            var tenantId = _tenantContext.TenantId;
            _tenant = tenantId.HasValue
                ? _db.Tenants.IgnoreQueryFilters().FirstOrDefault(t => t.Id == tenantId.Value)
                : null;
            _loaded = true;
        }

        return _tenant;
    }

    public string Name => Resolve()?.Name ?? "Unknown Organization";
    public string? Address => Resolve()?.Address;
    public string? ContactInfo => Resolve()?.ContactInfo;
}
