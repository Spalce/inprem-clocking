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
    private Tenant? _tenant;
    private bool _loaded;

    public CurrentTenantProfile(ApplicationDbContext db)
    {
        _db = db;
    }

    private Tenant? Resolve()
    {
        if (!_loaded)
        {
            _tenant = _db.Tenants.FirstOrDefault();
            _loaded = true;
        }

        return _tenant;
    }

    public string Name => Resolve()?.Name ?? "Unknown Organization";
    public string? Address => Resolve()?.Address;
    public string? ContactInfo => Resolve()?.ContactInfo;
}
