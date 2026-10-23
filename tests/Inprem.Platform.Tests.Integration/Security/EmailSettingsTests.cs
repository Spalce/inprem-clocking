using System.Net;
using System.Text;
using System.Text.Json;
using Inprem.Platform.Infrastructure.Twilio;
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

    private const string AccountSid = "AC00000000000000000000000000000001";
    private const string Token = "test-auth-token-not-real";

    /// <summary>A pretend Twilio Email API: keeps each request, answers with the next queued status (202 by default).</summary>
    private sealed class FakeTwilio : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
        public Queue<(HttpStatusCode Status, string Body)> Answers { get; } = new();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            var (status, body) = Answers.Count > 0 ? Answers.Dequeue() : (HttpStatusCode.Accepted, """{"operationId":"op_1"}""");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static EmailService Service(EmailOptions options, string environment = "Production", FakeTwilio? twilio = null, TwilioOptions? account = null) =>
        new(Options.Create(options), new Env(environment), twilio ?? new FakeTwilio(), Options.Create(account ?? new TwilioOptions()));

    private static EmailOptions TwilioEmail => new() { Provider = EmailProvider.Twilio, From = "no-reply@example.test", FromName = "Inprem Clocking" };
    private static TwilioOptions TwilioAccount => new() { AccountSid = AccountSid, AuthToken = Token };

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

    [Fact]
    public async Task Security_Email_Through_Twilio_Posts_To_The_Email_Api_With_The_Twilio_Account()
    {
        var twilio = new FakeTwilio();
        await Service(TwilioEmail, twilio: twilio, account: TwilioAccount).SendEmail("a@b.test", "Reset Password", "<a href='x'>link</a>");

        var (request, body) = Assert.Single(twilio.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://comms.twilio.com/v1/Emails", request.RequestUri!.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AccountSid}:{Token}")), request.Headers.Authorization!.ToString());

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("no-reply@example.test", json.GetProperty("from").GetProperty("address").GetString());
        Assert.Equal("Inprem Clocking", json.GetProperty("from").GetProperty("name").GetString());
        Assert.Equal("a@b.test", json.GetProperty("to")[0].GetProperty("address").GetString());
        Assert.Equal("Reset Password", json.GetProperty("content").GetProperty("subject").GetString());
        Assert.Equal("<a href='x'>link</a>", json.GetProperty("content").GetProperty("html").GetString());
    }

    [Fact]
    public async Task Security_Email_Through_Twilio_Uses_The_Api_Key_When_Set()
    {
        var twilio = new FakeTwilio();
        var account = new TwilioOptions { ApiKeySid = "SK00000000000000000000000000000001", ApiKeySecret = "key-secret-not-real" };
        await Service(TwilioEmail, twilio: twilio, account: account).SendEmail("a@b.test", "s", "m");
        var (request, _) = Assert.Single(twilio.Requests);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("SK00000000000000000000000000000001:key-secret-not-real")), request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Security_Email_Through_Twilio_Without_The_Account_Fails_With_A_Clear_Message()
    {
        var twilio = new FakeTwilio();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(TwilioEmail, twilio: twilio).SendEmail("a@b.test", "s", "m"));
        Assert.Contains("Twilio:AccountSid", error.Message);
        Assert.Empty(twilio.Requests);
    }

    [Fact]
    public async Task Security_Email_Refused_By_Twilio_Fails_Without_Repeating_The_Address()
    {
        var twilio = new FakeTwilio();
        twilio.Answers.Enqueue((HttpStatusCode.BadRequest, """{"code":20001,"message":"Sender a@b.test is not verified"}"""));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(TwilioEmail, twilio: twilio, account: TwilioAccount).SendEmail("a@b.test", "s", "m"));
        Assert.Contains("HTTP 400, error 20001", error.Message);
        Assert.DoesNotContain("a@b.test", error.Message);
        Assert.DoesNotContain(Token, error.Message);
    }

    [Fact]
    public async Task Security_Email_Uses_Smtp_Unless_Twilio_Is_Chosen()
    {
        var twilio = new FakeTwilio();
        // No SMTP settings: the default provider (SMTP) fails as before, and Twilio is not called even with its account set.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new EmailOptions(), twilio: twilio, account: TwilioAccount).SendEmail("a@b.test", "s", "m"));
        Assert.Empty(twilio.Requests);
    }
}
