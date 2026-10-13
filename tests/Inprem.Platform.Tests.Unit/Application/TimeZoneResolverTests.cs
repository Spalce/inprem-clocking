using Inprem.Platform.Application.Time;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Application;

public class TimeZoneResolverTests
{
    private static readonly DateTime JulyNoonUtc = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("America/New_York", -4)]
    [InlineData("Europe/London", 1)]
    [InlineData("America/Chicago", -5)]
    public void Resolves_Iana_Ids(string id, int julyOffsetHours)
    {
        var zone = TimeZoneResolver.Resolve(id);

        Assert.Equal(TimeSpan.FromHours(julyOffsetHours), zone.GetUtcOffset(JulyNoonUtc));
    }

    [Fact]
    public void Resolves_Windows_Ids_Too()
    {
        // On Linux and on Windows with ICU, .NET accepts both id styles.
        Assert.Equal(TimeSpan.FromHours(-4), TimeZoneResolver.Resolve("Eastern Standard Time").GetUtcOffset(JulyNoonUtc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Unknown_Ids_Fall_Back_To_New_York(string? id)
    {
        Assert.Equal(TimeSpan.FromHours(-4), TimeZoneResolver.Resolve(id).GetUtcOffset(JulyNoonUtc));
    }

    [Fact]
    public void A_Bad_Fallback_Ends_At_Utc()
    {
        Assert.Same(TimeZoneInfo.Utc, TimeZoneResolver.Resolve("Mars/Olympus_Mons", "Also/Not_A_Zone"));
    }

    [Fact]
    public void TryFind_Returns_Null_For_Unknown_Ids()
    {
        Assert.Null(TimeZoneResolver.TryFind("Mars/Olympus_Mons"));
        Assert.Null(TimeZoneResolver.TryFind(" "));
    }
}
