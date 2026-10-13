using InpremClockingApp.Tests.Support.Hosting;
using Xunit;

namespace InpremClockingApp.Tests.Baseline;

/// <summary>
/// One seeded app shared by every test class that only reads. Classes in this collection must
/// not change data; tests that write use their own <c>IClassFixture&lt;AppFixture&gt;</c>.
/// </summary>
[CollectionDefinition(Name)]
public class ReadOnlyAppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "Seeded app (read-only)";
}

/// <summary>
/// Trait names used across the baseline. A test tagged <c>Finding</c> records behaviour that
/// looks wrong but is today's approved behaviour (see docs/multitenancy/baseline-findings.md);
/// a test tagged <c>ChangedBy</c> records behaviour the design deliberately changes later.
/// When that change lands, the test is updated in the same step, with the reason.
/// </summary>
public static class Traits
{
    public const string Finding = "Finding";
    public const string ChangedBy = "ChangedBy";
}
