using System.Text.RegularExpressions;
using Xunit;

namespace Inprem.Platform.Tests.Architecture;

/// <summary>
/// Step 7.6: every read that looks past the organization filters (<c>IgnoreQueryFilters()</c>) was
/// reviewed; the reviewed ones are listed here, by file, with how many and why
/// (docs/multitenancy/security-review.md has the details). A new one, or one more in a listed file,
/// fails until it is reviewed and added. One that disappears must be removed from the list.
/// </summary>
public class CrossOrganizationReadTests
{
    private static readonly Dictionary<string, (int Count, string Why)> Reviewed = new()
    {
        ["InpremClockingApp/Data/AppUserStore.cs"] = (1, "Account look-up by id, user name or email for sign-in, password reset, the security stamp and invitations; listing stays filtered"),
        ["InpremClockingApp/Helpers/PlatformUserValidator.cs"] = (2, "An email is one account platform-wide (D2): the uniqueness check must see every account; answers yes or no only"),
        ["InpremClockingApp/Services/CurrentTenantProfile.cs"] = (1, "The current organization's own row, by the validated organization id"),
        ["InpremClockingApp/Services/TenantClock.cs"] = (1, "The current organization's time zone, by the validated organization id"),
        ["InpremClockingApp/StaffAlerts/StaffAlertSource.cs"] = (3, "The staff-alerts job (no signed-in organization): organizations with alerts on, then each one's open clockings, filtered by its id"),
        ["InpremClockingApp/Provider/Organizations/OrganizationStore.cs"] = (12, "The Provider Console (all organizations, behind console sign-in and roles); the public slug check only learns yes or no"),
    };

    private static readonly Regex Call = new(@"\.IgnoreQueryFilters\s*\(", RegexOptions.Compiled);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Calls in code (comments ignored), counted per file, as repository-relative paths with forward slashes.</summary>
    private static Dictionary<string, int> CallsByFile()
    {
        var root = RepositoryRoot();
        return new[] { "InpremClockingApp", "src" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".cshtml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (File: Path.GetRelativePath(root, f).Replace('\\', '/'),
                          Count: File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).Sum(l => Call.Matches(l).Count)))
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.File, x => x.Count);
    }

    [Fact]
    public void Every_Read_Past_The_Organization_Filters_Was_Reviewed()
    {
        var found = CallsByFile();
        var unreviewed = found.Where(f => !Reviewed.TryGetValue(f.Key, out var r) || f.Value > r.Count)
            .Select(f => $"{f.Key}: {f.Value} (reviewed: {(Reviewed.TryGetValue(f.Key, out var r) ? r.Count : 0)})").ToList();
        Assert.True(unreviewed.Count == 0,
            "IgnoreQueryFilters() not reviewed. Check that it cannot show or change another organization's data to the caller, "
            + "then add it to this list and to docs/multitenancy/security-review.md:\n" + string.Join("\n", unreviewed));
    }

    [Fact]
    public void The_Reviewed_List_Has_No_Stale_Entries()
    {
        var found = CallsByFile();
        var stale = Reviewed.Where(r => !found.TryGetValue(r.Key, out var n) || n < r.Value.Count).Select(r => $"{r.Key}: {(found.TryGetValue(r.Key, out var n) ? n : 0)} now, {r.Value.Count} listed").ToList();
        Assert.True(stale.Count == 0, "Fewer IgnoreQueryFilters() than listed; lower the count or remove the entry:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void The_Scan_Sees_The_Code()
    {
        Assert.True(CallsByFile().ContainsKey("InpremClockingApp/Data/AppUserStore.cs"), "The scan found nothing; it looks broken.");
    }
}
