using Xunit;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>C3-1 (step 1.1): the back-office sidebar no longer links to the old SuperAdmin pages.</summary>
public class SidebarTests
{
    [Fact]
    public void Sidebar_Has_No_Old_Platform_Links()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        var sidebar = File.ReadAllText(Path.Combine(dir!.FullName, "InpremClockingApp", "Pages", "Shared", "_Sidebar.cshtml"));
        Assert.DoesNotContain("/Platform/", sidebar);
        Assert.DoesNotContain("SuperAdminRole", sidebar);
    }
}
