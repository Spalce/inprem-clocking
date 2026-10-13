namespace InpremClockingApp.Tests.Support.TestData;

/// <summary>
/// What <see cref="TestDataSeeder"/> put in the database, so tests can refer to real ids and
/// accounts. All names, emails and phone numbers are invented (<c>.test</c> domain, 555 numbers).
/// </summary>
public sealed class SeedCatalog
{
    /// <summary>
    /// Inprem's own organization. Not seeded by the tests: migration
    /// <c>MultiTenancy_Phase1a_BackfillTenantData</c> inserts it into every new database.
    /// It has no people, admins or clockings here.
    /// </summary>
    public required TenantSeed House { get; init; }

    public required TenantSeed Alpha { get; init; }
    public required TenantSeed Beta { get; init; }

    /// <summary>An organization with no people, admins or clockings (for signup tests later).</summary>
    public required TenantSeed Gamma { get; init; }

    /// <summary>Generated per fixture; every seeded admin account uses it. Never a real credential.</summary>
    public required string Password { get; init; }

    public IEnumerable<TenantSeed> All => new[] { House, Alpha, Beta, Gamma };
}

public sealed class TenantSeed
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string TimeZoneId { get; init; }

    /// <summary>The first admin of the organization (becomes its Owner once memberships exist).</summary>
    public AdminSeed? Owner { get; init; }
    public AdminSeed? Admin { get; init; }

    public IReadOnlyList<PersonSeed> Staff { get; init; } = Array.Empty<PersonSeed>();
    public IReadOnlyList<PersonSeed> Volunteers { get; init; } = Array.Empty<PersonSeed>();

    /// <summary>Org-local calendar days on which every staff member and volunteer has a completed session.</summary>
    public IReadOnlyList<ClockingDay> ClockingDays { get; init; } = Array.Empty<ClockingDay>();
}

public sealed record AdminSeed(string UserId, string Email, string FirstName, string LastName);

public sealed record PersonSeed(long Id, string Email, string FirstName, string LastName)
{
    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>
/// One seeded working day. Times are org-local wall-clock values; the stored values are UTC.
/// <see cref="ExpectedWorkingHours"/> is a hand-computed constant, not derived from the times,
/// so tests compare the app against an independent expectation.
/// </summary>
public sealed record ClockingDay(
    DateOnly Date,
    TimeOnly ClockIn,
    TimeOnly BreakStart,
    TimeOnly BreakEnd,
    TimeOnly ClockOut,
    TimeSpan ExpectedWorkingHours,
    bool IsDaylightSavingChangeDay);
