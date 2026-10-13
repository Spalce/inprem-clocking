using Inprem.Platform.Domain.Memberships;
using InpremClockingApp.Data;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inprem.Platform.Tests.Architecture;

/// <summary>
/// Query-filter coverage (TESTING.md; Phase 0 step 0.5): every entity of the approved app's
/// context that carries a TenantId has a query filter and is covered by the save interceptor.
/// A new tenant-owned table that forgets either fails here.
/// </summary>
public class TenantFilterCoverageTests
{
    // Built without a database: only the model is read.
    private static readonly ApplicationDbContext Context = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=model-only;Database=none").Options,
        new NoTenant());

    // AppUser: TenantId is the account's first organization; visibility follows memberships
    // (its own filter) and the interceptor only forbids changing the column.
    // TenantMembership: a read-only view of platform.TenantMemberships, never saved through this context.
    private static readonly HashSet<Type> HandledSeparately = new() { typeof(AppUser), typeof(TenantMembership) };

    [Fact]
    public void Every_Entity_With_A_TenantId_Has_A_Query_Filter_And_Interceptor_Coverage()
    {
        var withTenant = Context.Model.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") != null && !e.IsOwned())
            .Select(e => e.ClrType)
            .ToList();

        Assert.True(withTenant.Count >= 6, "Expected at least the six tenant-owned tables; the model looks wrong.");
        foreach (var type in withTenant)
        {
            // The memberships view has no filter on purpose: the AppUser filter itself queries it.
            if (type != typeof(TenantMembership))
            {
                Assert.True(Context.Model.FindEntityType(type)!.GetQueryFilter() != null, $"{type.Name} has a TenantId but no query filter.");
            }
            if (!HandledSeparately.Contains(type))
            {
                Assert.True(TenantSaveChangesInterceptor.TenantOwned.Contains(type), $"{type.Name} has a TenantId but is not covered by TenantSaveChangesInterceptor.");
            }
        }
    }

    [Fact]
    public void The_Interceptor_Covers_Only_Real_Tenant_Owned_Entities()
    {
        foreach (var type in TenantSaveChangesInterceptor.TenantOwned)
        {
            var entity = Context.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.Equal(typeof(int), entity!.FindProperty("TenantId")!.ClrType);
        }
    }

    [Fact]
    public void Tenants_And_Users_Are_Filtered_Too()
    {
        Assert.NotNull(Context.Model.FindEntityType(typeof(Tenant))!.GetQueryFilter());
        Assert.NotNull(Context.Model.FindEntityType(typeof(AppUser))!.GetQueryFilter());
    }

    private sealed class NoTenant : ICurrentTenantService
    {
        public int? TenantId => null;
    }
}
