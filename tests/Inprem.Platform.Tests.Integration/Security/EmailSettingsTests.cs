using InpremClockingApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Security;

/// <summary>
/// P0-5: EmailService has no credentials in code. Settings come from configuration (Email:*);
/// without them it says so clearly; in Development, Email:DropFolder saves emails as files.
/// </summary>
public class EmailSettingsTests
{
    private sealed class Env : IWebHostEnvironment
    {
        public Env(string name) => EnvironmentName = name;
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "InpremClockingApp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; }
    }

    private static EmailService Service(EmailOptions options, string environment = "Production") =>
        new(Options.Create(options), new Env(environment));

    [Fact]
    public async Task Security_Email_Without_Settings_Fails_With_A_Clear_Message()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new EmailOptions()).SendEmail("a@b.test", "s", "m"));
        Assert.Contains("Email is not configured", error.Message);
    }

    [Fact]
    public async Task Security_Email_Port_465_Is_Explained()
    {
        var options = new EmailOptions { SmtpHost = "smtp.example.test", From = "no-reply@example.test", SmtpPort = 465 };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(options).SendEmail("a@b.test", "s", "m"));
        Assert.Contains("use 587", error.Message);
    }

    [Fact]
    public async Task Security_Email_Drop_Folder_Works_In_Development_Only()
    {
        var folder = Path.Combine(Path.GetTempPath(), "inprem-drop-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Service(new EmailOptions { DropFolder = folder }, "Development").SendEmail("a@b.test", "Reset Password", "<a href='x'>link</a>");
            var file = Assert.Single(Directory.GetFiles(folder));
            Assert.Contains("a@b.test", await File.ReadAllTextAsync(file));

            // Anywhere else the drop folder is ignored: without SMTP settings the send fails.
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new EmailOptions { DropFolder = folder }, "Production").SendEmail("a@b.test", "s", "m"));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Security_EmailService_Holds_No_Credentials()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "InpremClockingApp", "Services", "EmailService.cs"));
        Assert.DoesNotMatch(@"NetworkCredential\(\s*""", source);
        Assert.DoesNotMatch(@"new SmtpClient\(\s*""", source);
    }
}
