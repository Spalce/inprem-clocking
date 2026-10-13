using System.Collections.Concurrent;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Security;

/// <summary>
/// Phase 0 step 0.6 (C2-5, exit check P0-5): no account is created from configuration, and the
/// app never reads the old SeedAdmin / SeedSuperAdmin keys - checked at runtime by recording
/// every configuration key the app asks for while it starts and serves requests.
/// </summary>
public class NoSeededAccountsTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync(nameof(NoSeededAccountsTests));

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Security_Seed_Keys_Are_Never_Read_And_Create_No_Account()
    {
        var tracking = new TrackingSource(new Dictionary<string, string?>
        {
            ["SeedAdmin:Email"] = "seeded-admin@seed.test",
            ["SeedAdmin:Password"] = "Seed-Passw0rd!",
            ["SeedSuperAdmin:Email"] = "seeded-super@seed.test",
            ["SeedSuperAdmin:Password"] = "Seed-Passw0rd!",
        });
        await using var app = new InpremAppFactory(_db.ConnectionString, extraConfiguration: tracking);
        using var client = app.CreateClient();

        // Start-up (IdentitySeeder runs here) and a few requests through sign-in and the kiosk.
        await client.GetAsync("/Identity/Account/Login");
        await SignIn.PostLoginAsync(client, "seeded-admin@seed.test", "Seed-Passw0rd!");
        await client.GetAsync("/StaffAttendance");

        Assert.DoesNotContain(tracking.Requested, key =>
            key.StartsWith("SeedAdmin", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("SeedSuperAdmin", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(tracking.Requested); // the recorder does see the app's reads

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Null(await users.FindByEmailAsync("seeded-admin@seed.test"));
        Assert.Null(await users.FindByEmailAsync("seeded-super@seed.test"));
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        Assert.True(await roles.RoleExistsAsync("Admin"));
        Assert.True(await roles.RoleExistsAsync("SuperAdmin"));
    }

    /// <summary>An in-memory configuration source that records every key it is asked for.</summary>
    private sealed class TrackingSource : IConfigurationSource
    {
        private readonly IDictionary<string, string?> _values;

        public TrackingSource(IDictionary<string, string?> values) => _values = values;

        public ConcurrentBag<string> Requested { get; } = new();

        public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(this);

        private sealed class Provider : ConfigurationProvider
        {
            private readonly TrackingSource _source;

            public Provider(TrackingSource source)
            {
                _source = source;
                foreach (var (key, value) in source._values) Data[key] = value;
            }

            public override bool TryGet(string key, out string? value)
            {
                _source.Requested.Add(key);
                return base.TryGet(key, out value);
            }
        }
    }
}
