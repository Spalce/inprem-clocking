using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Inprem.Platform.Tests.Architecture;

/// <summary>
/// Dependencies point inwards (design section 3): Web -> Application -> Domain;
/// Infrastructure -> Application, Domain. Domain and Application stay free of framework code.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Inprem.Platform.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(Inprem.Platform.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(Inprem.Platform.Infrastructure.AssemblyMarker).Assembly;

    private const string EfCore = "Microsoft.EntityFrameworkCore";
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string Web = "InpremClockingApp";

    [Fact]
    public void Domain_Has_No_Framework_Or_Outer_Layer_Dependencies()
    {
        AssertNoDependency(Domain, EfCore, AspNetCore, Web, "Inprem.Platform.Application", "Inprem.Platform.Infrastructure");
    }

    [Fact]
    public void Application_Does_Not_Depend_On_Infrastructure_Web_Or_Frameworks()
    {
        AssertNoDependency(Application, EfCore, AspNetCore, Web, "Inprem.Platform.Infrastructure");
    }

    [Fact]
    public void Infrastructure_Does_Not_Depend_On_The_Web_Project()
    {
        AssertNoDependency(Infrastructure, Web);
    }

    [Fact]
    public void Project_References_Point_Inwards()
    {
        // Compiled references only list assemblies whose types are actually used, so these are
        // "nothing outside the allowed set" checks rather than exact lists.
        Assert.Empty(PlatformReferences(Domain));
        Assert.Empty(PlatformReferences(Application).Except(new[] { "Inprem.Platform.Domain" }));
        Assert.Empty(PlatformReferences(Infrastructure).Except(new[] { "Inprem.Platform.Domain", "Inprem.Platform.Application" }));
        Assert.DoesNotContain(Web, ReferencedAssemblyNames(Domain).Concat(ReferencedAssemblyNames(Application)).Concat(ReferencedAssemblyNames(Infrastructure)));
    }

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful,
            $"{assembly.GetName().Name} must not depend on {string.Join(", ", forbidden)}. Offending types: " +
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));

        var references = ReferencedAssemblyNames(assembly);
        var bad = references.Where(r => forbidden.Any(f => r == f || r.StartsWith(f + ".", StringComparison.Ordinal))).ToList();
        Assert.True(bad.Count == 0, $"{assembly.GetName().Name} references {string.Join(", ", bad)}.");
    }

    private static List<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

    private static List<string> PlatformReferences(Assembly assembly) =>
        ReferencedAssemblyNames(assembly).Where(n => n.StartsWith("Inprem.Platform.", StringComparison.Ordinal)).OrderBy(n => n).ToList();
}
