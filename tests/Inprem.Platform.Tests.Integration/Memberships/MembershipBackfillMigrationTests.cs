using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Memberships;

/// <summary>
/// Migration M2 on a database that already has accounts (as an existing deployment does):
/// one Active membership per account with an organization; the only admin of an organization is
/// its Owner; organizations with several admins get Admins only; operators get none; re-runnable.
/// </summary>
public class MembershipBackfillMigrationTests
{
    private const string M1a = "20261013080438_M1a_DataProtectionKeys";
    private const string M2 = "20261013083152_M2_TenantMemberships";

    [Fact]
    public async Task Migration_M2_Backfills_Memberships()
    {
        await using var db = await TestDatabase.CreateAsync(nameof(MembershipBackfillMigrationTests));
        await using var platform = db.CreatePlatformDbContext();
        var migrator = platform.GetService<IMigrator>();
        await migrator.MigrateAsync(M1a);

        int solo, crowded;
        string soloAdmin, crowdA, crowdB, operatorId;
        await using (var app = db.CreateDbContext())
        {
            var soloTenant = new Tenant { Name = "Solo" };
            var crowdedTenant = new Tenant { Name = "Crowded" };
            app.Tenants.AddRange(soloTenant, crowdedTenant);
            await app.SaveChangesAsync();
            (solo, crowded) = (soloTenant.Id, crowdedTenant.Id);

            AppUser User(int? tenantId)
            {
                var email = $"{Guid.NewGuid():N}@backfill.test";
                return new AppUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(), TenantId = tenantId };
            }
            var users = new[] { User(solo), User(crowded), User(crowded), User(null) };
            app.Users.AddRange(users);
            await app.SaveChangesAsync();
            (soloAdmin, crowdA, crowdB, operatorId) = (users[0].Id, users[1].Id, users[2].Id, users[3].Id);
        }

        await migrator.MigrateAsync(M2);

        // Only the columns M2 creates (later migrations add more, e.g. LastSignInUtc in M1d).
        var memberships = await platform.TenantMemberships.AsNoTracking()
            .Select(m => new Row(m.UserId, m.TenantId, m.Role, m.Status)).ToListAsync();
        Assert.Equal(3, memberships.Count);
        Assert.All(memberships, m => Assert.Equal(MembershipStatus.Active, m.Status));
        Assert.Equal((solo, MembershipRole.Owner), Single(memberships, soloAdmin));
        Assert.Equal((crowded, MembershipRole.Admin), Single(memberships, crowdA));
        Assert.Equal((crowded, MembershipRole.Admin), Single(memberships, crowdB));
        Assert.DoesNotContain(memberships, m => m.UserId == operatorId);

        // Down then up again gives the same result (re-runnable).
        await migrator.MigrateAsync(M1a);
        Assert.Equal(0, await platform.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'TenantMemberships'").SingleAsync());
        await migrator.MigrateAsync(M2);
        Assert.Equal(3, await platform.TenantMemberships.CountAsync());
    }

    private sealed record Row(string UserId, int TenantId, MembershipRole Role, MembershipStatus Status);

    [Fact]
    public async Task Migration_M2_Foreign_Keys_Protect_The_Data()
    {
        await using var db = await TestDatabase.CreateAsync(nameof(MembershipBackfillMigrationTests) + "Fk");
        await using var platform = db.CreatePlatformDbContext();

        platform.TenantMemberships.Add(TenantMembership.CreateActive("no-such-user", 1, MembershipRole.Admin, DateTime.UtcNow));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => platform.SaveChangesAsync());
        Assert.Contains("FK_TenantMemberships_AspNetUsers_UserId", error.InnerException!.Message);
    }

    private static (int TenantId, MembershipRole Role) Single(List<Row> memberships, string userId)
    {
        var membership = Assert.Single(memberships, m => m.UserId == userId);
        return (membership.TenantId, membership.Role);
    }
}
