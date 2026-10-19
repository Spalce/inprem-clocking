using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Notifications;

/// <summary>
/// Phase 5 step 5.1: the Twilio sender against a pretend Twilio (no network, no credentials): what is
/// sent, how answers are read, the request signature, the settings check, and that numbers and
/// credentials never reach the logs. The stand-in is the default.
/// </summary>
public class TwilioClientTests
{
    private const string AccountSid = "AC00000000000000000000000000000001";
    private const string ServiceSid = "MG00000000000000000000000000000001";
    private const string Token = "test-auth-token-not-real";
    private const string Phone = "+12025550123";

    private sealed record Seen(string Path, string Authorization, Dictionary<string, string> Form);

    private sealed class FakeTwilio : HttpMessageHandler
    {
        public ConcurrentQueue<Seen> Requests { get; } = new();
        public Queue<(HttpStatusCode Status, string Body)> Answers { get; } = new();
        public bool Unreachable { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Unreachable) throw new HttpRequestException("No route to host.");
            var body = await request.Content!.ReadAsStringAsync(ct);
            var form = body.Split('&').Select(p => p.Split('=')).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            Requests.Enqueue(new Seen(request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString() ?? "", form));
            var (status, text) = Answers.Count > 0 ? Answers.Dequeue()
                : (HttpStatusCode.Created, """{"sid":"SM0123456789abcdef0123456789abcdef","status":"accepted","to":"+12025550123"}""");
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }

        private sealed class Logger : ILogger
        {
            private readonly CapturedLogs _owner;
            public Logger(CapturedLogs owner) => _owner = owner;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _owner.Lines.Enqueue(formatter(state, exception));
        }
    }

    private static (ServiceProvider Services, FakeTwilio Fake, CapturedLogs Logs) Create(Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Twilio:Mode"] = "Twilio", ["Twilio:AccountSid"] = AccountSid, ["Twilio:AuthToken"] = Token,
            ["Twilio:MessagingServiceSid"] = ServiceSid, ["Twilio:WebhookBaseUrl"] = "https://app.inprem.test",
        };
        foreach (var (k, v) in settings ?? new()) values[k] = v;
        var fake = new FakeTwilio();
        var logs = new CapturedLogs();
        var services = new ServiceCollection()
            .AddLogging(l => l.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace))
            .AddTwilio(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddHttpClient(TwilioSmsSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        return (services.BuildServiceProvider(), fake, logs);
    }

    [Fact]
    public async Task A_Text_Goes_Through_The_Messaging_Service_With_The_Status_Callback()
    {
        var (sp, fake, logs) = Create();
        using var _ = sp;
        var sms = sp.GetRequiredService<ISmsSender>();
        Assert.False(sms.IsStandIn);

        var result = await sms.SendAsync(Phone, "Inprem Clock-in: test message.");

        Assert.Equal(SmsSendResult.Ok("SM0123456789abcdef0123456789abcdef"), result);
        var seen = Assert.Single(fake.Requests);
        Assert.Equal($"/2010-04-01/Accounts/{AccountSid}/Messages.json", seen.Path);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AccountSid}:{Token}")), seen.Authorization);
        Assert.Equal(ServiceSid, seen.Form["MessagingServiceSid"]);
        Assert.Equal((Phone, "Inprem Clock-in: test message."), (seen.Form["To"], seen.Form["Body"]));
        Assert.Equal("https://app.inprem.test/webhooks/twilio/status", seen.Form["StatusCallback"]);

        // Neither the number nor the credentials reach the logs.
        Assert.DoesNotContain(logs.Lines, l => l.Contains("5550123") || l.Contains(Token) || l.Contains("Basic "));
        Assert.Contains(logs.Lines, l => l.Contains("+1202•••0123"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"code":20429,"message":"Too Many Requests"}""", true, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>down</html>", true, false)]
    [InlineData(HttpStatusCode.BadRequest, """{"code":21211,"message":"Invalid 'To' Phone Number"}""", false, false)]
    [InlineData(HttpStatusCode.BadRequest, """{"code":21610,"message":"Attempt to send to unsubscribed recipient"}""", false, true)]
    public async Task Refusals_Say_Whether_To_Try_Again(HttpStatusCode status, string body, bool temporary, bool optedOut)
    {
        var (sp, fake, _) = Create();
        using var __ = sp;
        fake.Answers.Enqueue((status, body));

        var result = await sp.GetRequiredService<ISmsSender>().SendAsync(Phone, "x");

        Assert.False(result.Accepted);
        Assert.Equal((temporary, optedOut), (result.Temporary, result.RecipientOptedOut));
        Assert.Single(fake.Requests); // never retried by the client: a retried send could text twice
    }

    [Fact]
    public async Task Twilio_Out_Of_Reach_Is_Temporary()
    {
        var (sp, fake, _) = Create();
        using var __ = sp;
        fake.Unreachable = true;
        var result = await sp.GetRequiredService<ISmsSender>().SendAsync(Phone, "x");
        Assert.Equal((false, true), (result.Accepted, result.Temporary));
    }

    [Fact]
    public async Task The_Stand_In_Is_The_Default_And_Sends_Nothing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "inprem-standin-" + Guid.NewGuid().ToString("N"));
        var (sp, fake, _) = Create(new() { ["Twilio:Mode"] = null, ["Twilio:AccountSid"] = null, ["Twilio:AuthToken"] = null, ["Twilio:MessagingServiceSid"] = null, ["Twilio:StandInFolder"] = folder });
        using var __ = sp;
        try
        {
            var sms = sp.GetRequiredService<ISmsSender>();
            Assert.True(sms.IsStandIn);
            var result = await sms.SendAsync(Phone, "Hello from the stand-in.");
            Assert.True(result.Accepted);
            Assert.StartsWith("SMstandin", result.ExternalId);
            Assert.Empty(fake.Requests);
            Assert.Contains("Hello from the stand-in.", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(folder))));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Twilio:AccountSid", "not-a-sid", "Twilio:AccountSid")]
    [InlineData("Twilio:AuthToken", "", "Twilio:AuthToken")]
    [InlineData("Twilio:MessagingServiceSid", "PN000", "Twilio:MessagingServiceSid")]
    [InlineData("Twilio:WebhookBaseUrl", "http://app.inprem.test", "Twilio:WebhookBaseUrl")]
    [InlineData("Twilio:WebhookBaseUrl", "https://app.inprem.test/sub", "Twilio:WebhookBaseUrl")]
    public void Wrong_Settings_Stop_The_App_At_Startup(string key, string value, string named)
    {
        var (sp, _, _) = Create(new() { [key] = value });
        using var __ = sp;
        var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<TwilioOptions>>().Value);
        Assert.Contains(error.Failures, f => f.Contains(named));
    }

    [Fact]
    public void Signatures_Follow_Twilios_Algorithm()
    {
        // Twilio's own documented example (auth token 12345).
        var form = new Dictionary<string, string>
        {
            ["CallSid"] = "CA1234567890ABCDE", ["Caller"] = "+12349013030", ["Digits"] = "1234", ["From"] = "+12349013030", ["To"] = "+18005551212",
        };
        const string url = "https://mycompany.com/myapp.php?foo=1&bar=2";
        Assert.Equal("0/KCTR6DLpKmkAf8muzZqo1nDgQ=", TwilioSignature.Compute("12345", url, form));
        Assert.True(TwilioSignature.IsValid("12345", url, form, "0/KCTR6DLpKmkAf8muzZqo1nDgQ="));
        Assert.False(TwilioSignature.IsValid("12345", url, form, "0/KCTR6DLpKmkAf8muzZqo1nDgQ"));
        Assert.False(TwilioSignature.IsValid("54321", url, form, "0/KCTR6DLpKmkAf8muzZqo1nDgQ="));
        Assert.False(TwilioSignature.IsValid("12345", url, new Dictionary<string, string>(form) { ["Digits"] = "9999" }, "0/KCTR6DLpKmkAf8muzZqo1nDgQ="));
        Assert.False(TwilioSignature.IsValid(null, url, form, "0/KCTR6DLpKmkAf8muzZqo1nDgQ="));
    }
}
