using System.Net;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Memberships;
using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Isolation;

/// <summary>
/// Step 0.5 rules, exercised through the app's own ApplicationDbContext as a request for
/// Alpha sees it: the save interceptor, and the fail-closed AppUser query filter.
/// </summary>
public class SaveAndFilterRulesTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public SaveAndFilterRulesTests(AppFixture app) => _app = app;

    /// <summary>A DI scope whose tenant context is set as the middleware would set it.</summary>
    private IServiceScope ScopeFor(int? tenantId)
    {
        var scope = _app.Services.CreateScope();
        if (tenantId.HasValue)
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>()
                .Set(new ActiveMembershipInfo(Guid.NewGuid(), "test", tenantId.Value, "test", MembershipRole.Admin));
        }
        return scope;
    }

    private static ApplicationDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private static Staff NewStaff(string tag) => new()
    {
        EmailAddress = $"{tag}-{Guid.NewGuid():N}"[..20] + "@save.test", FirstName = "Save", LastName = "Rules",
        ZipCode = "43229", Gender = 'F', PhoneNumber = "6145550111", Address = "1 Rules Road, Testville",
    };

    private async Task<string> BetaStaffNameAsync(long id)
    {
        await using var db = _app.CreateDbContext();
        return (await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id)).FirstName!;
    }

    [Fact]
    public async Task Save_New_Row_Is_Stamped_With_The_Current_Organization()
    {
        using var scope = ScopeFor(_app.Data.Alpha.Id);
        var staff = NewStaff("stamp");
        Db(scope).Staffs.Add(staff);

        await Db(scope).SaveChangesAsync();

        Assert.Equal(_app.Data.Alpha.Id, staff.TenantId);
    }

    [Fact]
    public async Task Save_New_Row_For_Another_Organization_Is_Refused()
    {
        using var scope = ScopeFor(_app.Data.Alpha.Id);
        var staff = NewStaff("foreign");
        staff.TenantId = _app.Data.Beta.Id;
        Db(scope).Staffs.Add(staff);

        await Assert.ThrowsAsync<TenantIsolationException>(() => Db(scope).SaveChangesAsync());
    }

    [Fact]
    public async Task Save_Change_To_Another_Organizations_Row_Is_Refused()
    {
        var betaStaff = _app.Data.Beta.Staff[1].Id;
        using var scope = ScopeFor(_app.Data.Alpha.Id);
        var row = await Db(scope).Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == betaStaff);
        row.FirstName = "Hijacked";

        await Assert.ThrowsAsync<TenantIsolationException>(() => Db(scope).SaveChangesAsync());
        Assert.NotEqual("Hijacked", await BetaStaffNameAsync(betaStaff));
    }

    [Fact]
    public async Task Save_Delete_Of_Another_Organizations_Row_With_A_Forged_TenantId_Is_Refused()
    {
        // As the Move handler does: an entity built from posted values, claiming to be Alpha's.
        var betaStaff = _app.Data.Beta.Staff[2].Id;
        using var scope = ScopeFor(_app.Data.Alpha.Id);
        Db(scope).Staffs.Remove(new Staff { StaffId = betaStaff, TenantId = _app.Data.Alpha.Id, EmailAddress = "forged@x.test" });

        await Assert.ThrowsAsync<TenantIsolationException>(() => Db(scope).SaveChangesAsync());
        await using var check = _app.CreateDbContext();
        Assert.True(await check.Staffs.IgnoreQueryFilters().AnyAsync(s => s.StaffId == betaStaff));
    }

    [Fact]
    public async Task Save_Changing_The_TenantId_Is_Refused_Even_Without_An_Organization()
    {
        var id = await AddAlphaStaffAsync();
        using var scope = ScopeFor(tenantId: null);
        var row = await Db(scope).Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id);
        row.TenantId = _app.Data.Beta.Id;

        // Staff.TenantId is part of a key since M4, so EF itself already refuses the change; the
        // interceptor's own refusal is a subclass of the same exception type.
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Db(scope).SaveChangesAsync());
    }

    [Fact]
    public async Task Save_Changing_A_Sessions_TenantId_Is_Refused_By_The_Interceptor()
    {
        // ClockingStaff.TenantId is not a key column of its own row, so this is the interceptor's rule.
        using var scope = ScopeFor(tenantId: null);
        var session = await Db(scope).ClockingsStaff.IgnoreQueryFilters().FirstAsync(c => c.TenantId == _app.Data.Alpha.Id);
        session.TenantId = _app.Data.Beta.Id;

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Db(scope).SaveChangesAsync());
        Assert.True(error is TenantIsolationException || error.Message.Contains("part of a key"), error.Message);
    }

    [Fact]
    public async Task Save_Own_Rows_Change_And_Delete_As_Before()
    {
        var id = await AddAlphaStaffAsync();
        using var scope = ScopeFor(_app.Data.Alpha.Id);
        var row = await Db(scope).Staffs.SingleAsync(s => s.StaffId == id);
        row.FirstName = "Renamed";
        await Db(scope).SaveChangesAsync();

        Db(scope).Staffs.Remove(row);
        await Db(scope).SaveChangesAsync();

        await using var check = _app.CreateDbContext();
        Assert.False(await check.Staffs.IgnoreQueryFilters().AnyAsync(s => s.StaffId == id));
    }

    [Fact]
    public async Task Save_Another_Organizations_Tenant_Record_Is_Refused()
    {
        using var scope = ScopeFor(_app.Data.Alpha.Id);
        var beta = await Db(scope).Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _app.Data.Beta.Id);
        beta.Name = "Hijacked";

        await Assert.ThrowsAsync<TenantIsolationException>(() => Db(scope).SaveChangesAsync());
    }

    [Fact]
    public async Task Users_Without_An_Organization_Nobody_Is_Visible()
    {
        using var scope = ScopeFor(tenantId: null);

        Assert.Equal(0, await Db(scope).Users.CountAsync());
        // Lookups by id/name/email still work: sign-in depends on them (AppUserStore).
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.NotNull(await users.FindByEmailAsync(_app.Data.Beta.Admin!.Email));
        Assert.NotNull(await users.FindByNameAsync(_app.Data.Alpha.Admin!.Email));
        Assert.NotNull(await users.FindByIdAsync(_app.Data.Alpha.Owner!.UserId));
    }

    [Fact]
    public async Task Users_Visible_Are_The_Organizations_Members()
    {
        // An Alpha account invited to Beta and accepted; a Beta account whose membership was revoked.
        var (crossId, _) = await CreateAccountAsync(_app.Data.Alpha.Id);
        var (revokedId, _) = await CreateAccountAsync(_app.Data.Beta.Id);
        using (var setup = _app.Services.CreateScope())
        {
            var memberships = setup.ServiceProvider.GetRequiredService<IMembershipService>();
            await memberships.AddMemberAsync(crossId, _app.Data.Alpha.Id, MembershipRole.Admin);
            await memberships.InviteAsync(crossId, _app.Data.Beta.Id, MembershipRole.Admin, _app.Data.Beta.Owner!.UserId);
            await memberships.AcceptAsync(crossId, (await memberships.ListInvitationsAsync(crossId)).Single().MembershipId);
        }
        await using (var platform = _app.CreatePlatformDbContext())
        {
            var revoked = TenantMembership.CreateActive(revokedId, _app.Data.Beta.Id, MembershipRole.Admin, DateTime.UtcNow);
            revoked.Revoke();
            platform.TenantMemberships.Add(revoked);
            await platform.SaveChangesAsync();
        }

        using var scope = ScopeFor(_app.Data.Beta.Id);
        var visible = await Db(scope).Users.Select(u => u.Id).ToListAsync();

        Assert.Contains(_app.Data.Beta.Owner!.UserId, visible);
        Assert.Contains(_app.Data.Beta.Admin!.UserId, visible);
        Assert.Contains(crossId, visible);
        Assert.DoesNotContain(revokedId, visible);
        Assert.DoesNotContain(_app.Data.Alpha.Admin!.UserId, visible);
    }

    [Fact]
    public async Task Users_A_Member_From_Another_Organization_Can_Open_Their_Own_Account_Page()
    {
        // The step 0.4 gap: before 0.5 the account was invisible in the organization it joined.
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        await using (var platform = _app.CreatePlatformDbContext())
        {
            var revokedHome = TenantMembership.CreateActive(userId, _app.Data.Alpha.Id, MembershipRole.Admin, DateTime.UtcNow);
            revokedHome.Revoke();
            platform.TenantMemberships.Add(revokedHome);
            platform.TenantMemberships.Add(TenantMembership.CreateActive(userId, _app.Data.Beta.Id, MembershipRole.Admin, DateTime.UtcNow));
            await platform.SaveChangesAsync();
        }
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);

        using var manage = await client.GetAsync("/Identity/Account/Manage");

        Assert.Equal(HttpStatusCode.OK, manage.StatusCode);
        Assert.Contains("staff1@beta.test", await (await client.GetAsync("/Staff")).Content.ReadAsStringAsync());
    }

    private async Task<long> AddAlphaStaffAsync()
    {
        await using var db = _app.CreateDbContext();
        var staff = NewStaff("own");
        staff.TenantId = _app.Data.Alpha.Id;
        db.Staffs.Add(staff);
        await db.SaveChangesAsync();
        return staff.StaffId;
    }

    private async Task<(string Id, string Email)> CreateAccountAsync(int tenantId)
    {
        var email = $"f-{Guid.NewGuid():N}"[..18] + "@filter.test";
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = tenantId, Type = IdentitySeeder.AdminRole };
        Assert.True((await users.CreateAsync(user, _app.Data.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, IdentitySeeder.AdminRole)).Succeeded);
        return (user.Id, email);
    }
}
