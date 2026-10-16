using Microsoft.Playwright;
using Xunit;

namespace InpremClockingApp.Tests.Ui.Console;

/// <summary>
/// The console's button-style links ("+ New organization", "+ New plan", "+ New invoice") are
/// readable without hovering (found in the developer's manual testing, 2026-10-16: the general link
/// colour won over the button colour, leaving blue text on the blue button).
/// </summary>
[Trait("Category", "Ui")]
public class ConsoleStyleTests
{
    private static string ConsoleCss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "InpremClockingApp", "wwwroot", "provider", "console.css"));
    }

    [Fact]
    public async Task Button_Links_Show_Their_Text_Without_Hovering()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync($"""
            <html><head><style>{ConsoleCss()}</style></head>
            <body class="pc"><main class="pc-content">
              <a id="primary" class="pc-btn pc-btn-primary" href="#">+ New organization</a>
              <a id="plain" class="pc-btn" href="#">Edit</a>
              <a id="danger" class="pc-btn pc-btn-danger" href="#">Suspend</a>
              <button id="button" class="pc-btn pc-btn-primary">Create</button>
            </main></body></html>
            """);

        foreach (var id in new[] { "primary", "plain", "danger", "button" })
        {
            var (color, background) = await ColorsAsync(page, id);
            Assert.True(color != background, $"#{id}: text {color} on background {background}");
        }
        // The primary link looks like the primary button: white text.
        Assert.Equal((await ColorsAsync(page, "button")).Color, (await ColorsAsync(page, "primary")).Color);
    }

    private static async Task<(string Color, string Background)> ColorsAsync(IPage page, string id)
    {
        var colors = await page.EvalOnSelectorAsync<string[]>($"#{id}", "e => { const s = getComputedStyle(e); return [s.color, s.backgroundColor]; }");
        return (colors[0], colors[1]);
    }
}
