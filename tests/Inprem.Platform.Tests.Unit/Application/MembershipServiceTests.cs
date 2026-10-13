using Inprem.Platform.Application;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Memberships;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Application;

public class MembershipServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeStore _store = new();
    private readonly MembershipService _service;

    public MembershipServiceTests() =>
        _service = new MembershipService(_store, new MemoryCache(new MemoryCacheOptions()), new FixedClock(Now));

    private TenantMembership Add(string userId, int tenantId, MembershipStatus status = MembershipStatus.Active)
    {
        var membership = TenantMembership.CreateActive(userId, tenantId, MembershipRole.Admin, Now);
        if (status == MembershipStatus.Revoked) membership.Revoke();
        _store.Memberships.Add(membership);
        return membership;
    }

    [Fact]
    public async Task Validate_Returns_An_Active_Membership_Of_The_User()
    {
        var membership = Add("u1", 1);

        var result = await _service.ValidateAsync("u1", membership.Id);

        Assert.Equal((membership.Id, 1, "Tenant 1"), (result!.MembershipId, result.TenantId, result.TenantName));
    }

    [Fact]
    public async Task Validate_Rejects_Another_Users_Membership_Even_When_Cached()
    {
        var membership = Add("u1", 1);
        await _service.ValidateAsync("u1", membership.Id);

        Assert.Null(await _service.ValidateAsync("intruder", membership.Id));
    }

    [Fact]
    public async Task Validate_Rejects_Revoked_And_Unknown_Memberships()
    {
        var revoked = Add("u1", 1, MembershipStatus.Revoked);

        Assert.Null(await _service.ValidateAsync("u1", revoked.Id));
        Assert.Null(await _service.ValidateAsync("u1", Guid.NewGuid()));
    }

    [Fact]
    public async Task Validate_Is_Cached_Until_Invalidated()
    {
        var membership = Add("u1", 1);
        await _service.ValidateAsync("u1", membership.Id);
        membership.Revoke();

        Assert.NotNull(await _service.ValidateAsync("u1", membership.Id));
        Assert.Equal(1, _store.FindCalls);

        _service.Invalidate(membership.Id);

        Assert.Null(await _service.ValidateAsync("u1", membership.Id));
        Assert.Equal(2, _store.FindCalls);
    }

    [Fact]
    public async Task Failed_Validation_Is_Not_Cached()
    {
        // An invitation is not valid yet; once accepted it is, without waiting for a cache entry.
        var membership = TenantMembership.CreateInvited("u1", 1, MembershipRole.Admin, "owner", Now);
        _store.Memberships.Add(membership);
        Assert.Null(await _service.ValidateAsync("u1", membership.Id));
        membership.Activate();

        Assert.NotNull(await _service.ValidateAsync("u1", membership.Id));
    }

    [Fact]
    public async Task SignIn_Returns_Existing_Active_Memberships()
    {
        Add("u1", 2);
        Add("u1", 1);

        var result = await _service.GetForSignInAsync("u1", accountTenantId: 1);

        Assert.Equal(new[] { 1, 2 }, result.Select(m => m.TenantId));
        Assert.Equal(0, _store.AddCalls);
    }

    [Fact]
    public async Task SignIn_Creates_An_Admin_Membership_For_A_Legacy_Account()
    {
        var result = await _service.GetForSignInAsync("legacy", accountTenantId: 4);

        var created = Assert.Single(result);
        Assert.Equal((4, MembershipRole.Admin), (created.TenantId, created.Role));
        var stored = Assert.Single(_store.Memberships);
        Assert.Equal((MembershipStatus.Active, Now), (stored.Status, stored.CreatedUtc));
    }

    [Fact]
    public async Task SignIn_Never_Recreates_A_Revoked_Membership()
    {
        Add("u1", 4, MembershipStatus.Revoked);

        var result = await _service.GetForSignInAsync("u1", accountTenantId: 4);

        Assert.Empty(result);
        Assert.Equal(0, _store.AddCalls);
    }

    [Fact]
    public async Task SignIn_Without_An_Organization_Creates_Nothing()
    {
        Assert.Empty(await _service.GetForSignInAsync("operator", accountTenantId: null));
        Assert.Equal(0, _store.AddCalls);
    }

    [Fact]
    public async Task ListActive_Passes_Through()
    {
        Add("u1", 3);

        Assert.Single(await _service.ListActiveAsync("u1"));
    }

    [Fact]
    public void TenantContext_Is_Set_Once_Per_Request()
    {
        var context = new TenantContext();
        var membership = new ActiveMembershipInfo(Guid.NewGuid(), "u1", 5, "Five", MembershipRole.Owner);
        ITenantContext view = context;
        Assert.False(view.HasTenant);

        context.Set(membership);
        context.Set(membership); // the error page re-runs the pipeline with the same membership

        Assert.Equal((5, membership.MembershipId, MembershipRole.Owner), (view.TenantId, view.MembershipId, view.Role));
        Assert.True(view.HasTenant);
        Assert.Throws<InvalidOperationException>(() => context.Set(membership with { MembershipId = Guid.NewGuid(), TenantId = 6 }));
    }

    [Fact]
    public void AddPlatformApplication_Registers_One_Context_Per_Request()
    {
        var services = new ServiceCollection().AddPlatformApplication();
        services.AddSingleton<IMembershipStore>(_store).AddSingleton<IClock>(new FixedClock(Now)).AddMemoryCache();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Same(scope.ServiceProvider.GetRequiredService<TenantContext>(), scope.ServiceProvider.GetRequiredService<ITenantContext>());
        Assert.IsType<MembershipService>(scope.ServiceProvider.GetRequiredService<IMembershipService>());
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; }
    }

    private sealed class FakeStore : IMembershipStore
    {
        public List<TenantMembership> Memberships { get; } = new();
        public int FindCalls { get; private set; }
        public int AddCalls { get; private set; }

        private static ActiveMembershipInfo Info(TenantMembership m) => new(m.Id, m.UserId, m.TenantId, $"Tenant {m.TenantId}", m.Role);

        public Task<ActiveMembershipInfo?> FindActiveAsync(Guid membershipId, string userId, CancellationToken cancellationToken = default)
        {
            FindCalls++;
            var m = Memberships.FirstOrDefault(x => x.Id == membershipId && x.UserId == userId && x.IsActive);
            return Task.FromResult(m == null ? null : Info(m));
        }

        public Task<IReadOnlyList<ActiveMembershipInfo>> ListActiveAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActiveMembershipInfo>>(Memberships.Where(m => m.UserId == userId && m.IsActive)
                .OrderBy(m => m.TenantId).Select(Info).ToList());

        public Task<bool> ExistsAsync(string userId, int tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Memberships.Any(m => m.UserId == userId && m.TenantId == tenantId));

        public Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            Memberships.Add(membership);
            return Task.CompletedTask;
        }
    }
}
