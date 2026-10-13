using System.Text.RegularExpressions;
using Xunit;

namespace Inprem.Platform.Tests.Architecture;

/// <summary>
/// Phase 0 step 0.6 (exit check P0-5): no code or configuration file refers to the removed
/// SeedAdmin / SeedSuperAdmin keys (source half; the runtime half is in the Integration tests).
/// Comments may mention them historically; string literals and JSON keys may not.
/// </summary>
public class NoSeedConfigurationTests
{
    private static readonly Regex KeyInCode = new("[\"']Seed(Super)?Admin", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepositoryRoot();
        return new[] { "InpremClockingApp", "src" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal) || f.EndsWith(".cshtml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    [Fact]
    public void No_Source_Or_Configuration_File_Uses_The_Seed_Keys()
    {
        var files = SourceFiles().ToList();
        Assert.True(files.Count > 50, "Too few files scanned; the scan looks broken.");

        var offenders = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.Text.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Where(l => KeyInCode.IsMatch(l.Text))
            .Select(l => $"{Path.GetFileName(l.File)}:{l.Line}")
            .ToList();

        Assert.True(offenders.Count == 0, "SeedAdmin/SeedSuperAdmin still used in: " + string.Join(", ", offenders));
    }
}
