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
    private const string VerifySid = "VA00000000000000000000000000000001";
    private const string Token = "test-auth-token-not-real";
    private const string Phone = "+12025550123";
    private const string FromNumber = "+12125550199";

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

    private static (ServiceProvider Services, FakeTwilio Fake, CapturedLogs Logs) Create(Dictionary<string, string?>? settings = null, Provider.TestClock? clock = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Twilio:Mode"] = "Twilio", ["Twilio:AccountSid"] = AccountSid, ["Twilio:AuthToken"] = Token,
            ["Twilio:MessagingServiceSid"] = ServiceSid, ["Twilio:VerifyServiceSid"] = VerifySid, ["Twilio:WebhookBaseUrl"] = "https://app.inprem.test",
        };
        foreach (var (k, v) in settings ?? new()) values[k] = v;
        var fake = new FakeTwilio();
        var logs = new CapturedLogs();
        var services = new ServiceCollection()
            .AddLogging(l => l.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace))
            .AddTwilio(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddHttpClient(TwilioSmsSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        services.AddHttpClient(TwilioVerifyOtpService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => fake);
        services.AddMemoryCache().AddSingleton<Inprem.Platform.Application.Memberships.IClock>(clock ?? new Provider.TestClock());
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
    [InlineData("Twilio:VerifyServiceSid", "VA000", "Twilio:VerifyServiceSid")]
    [InlineData("Twilio:FromNumber", "0244123456", "Twilio:FromNumber")]
    [InlineData("Twilio:ApiKeySid", "SK00000000000000000000000000000001", "Twilio:ApiKeySecret")]
    [InlineData("Twilio:ApiKeySecret", "key-secret-not-real", "Twilio:ApiKeySid")]
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
    public void Twilio_Mode_Needs_A_Messaging_Service_Or_A_From_Number()
    {
        var (sp, _, _) = Create(new() { ["Twilio:MessagingServiceSid"] = null });
        using var __ = sp;
        var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<TwilioOptions>>().Value);
        Assert.Contains(error.Failures, f => f.Contains("Twilio:MessagingServiceSid or Twilio:FromNumber"));
    }

    [Fact]
    public async Task Without_A_Messaging_Service_A_Text_Goes_From_The_Twilio_Number()
    {
        var (sp, fake, _) = Create(new() { ["Twilio:MessagingServiceSid"] = null, ["Twilio:FromNumber"] = FromNumber });
        using var __ = sp;
        Assert.True((await sp.GetRequiredService<ISmsSender>().SendAsync(Phone, "x")).Accepted);
        var seen = Assert.Single(fake.Requests);
        Assert.Equal($"/2010-04-01/Accounts/{AccountSid}/Messages.json", seen.Path);
        Assert.Equal(FromNumber, seen.Form["From"]);
        Assert.False(seen.Form.ContainsKey("MessagingServiceSid"));
    }

    [Fact]
    public async Task With_An_Api_Key_Texts_And_Codes_Sign_In_With_The_Key()
    {
        const string KeySid = "SK00000000000000000000000000000001", KeySecret = "key-secret-not-real";
        var (sp, fake, logs) = Create(new() { ["Twilio:ApiKeySid"] = KeySid, ["Twilio:ApiKeySecret"] = KeySecret });
        using var __ = sp;
        await sp.GetRequiredService<ISmsSender>().SendAsync(Phone, "x");
        await sp.GetRequiredService<IOtpService>().SendAsync(Phone);

        var key = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeySid}:{KeySecret}"));
        Assert.All(fake.Requests, r => Assert.Equal(key, r.Authorization));
        Assert.Equal($"/2010-04-01/Accounts/{AccountSid}/Messages.json", fake.Requests.First().Path); // the account still names the address
        Assert.DoesNotContain(logs.Lines, l => l.Contains(KeySecret));
    }

    [Fact]
    public async Task The_Messaging_Service_Wins_When_Both_Are_Set()
    {
        var (sp, fake, _) = Create(new() { ["Twilio:FromNumber"] = FromNumber });
        using var __ = sp;
        await sp.GetRequiredService<ISmsSender>().SendAsync(Phone, "x");
        var seen = Assert.Single(fake.Requests);
        Assert.Equal(ServiceSid, seen.Form["MessagingServiceSid"]);
        Assert.False(seen.Form.ContainsKey("From"));
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

    // ------------------------------------------------------------ login codes (step 5.2)

    [Fact]
    public async Task Verify_Sends_The_Code_By_Text_And_Reads_The_Answers()
    {
        var (sp, fake, logs) = Create();
        using var _ = sp;
        var otp = sp.GetRequiredService<IOtpService>();
        Assert.False(otp.IsStandIn);

        fake.Answers.Enqueue((HttpStatusCode.Created, """{"sid":"VE1","status":"pending","to":"+12025550123","channel":"sms"}"""));
        Assert.True((await otp.SendAsync(Phone)).Sent);
        var send = Assert.Single(fake.Requests);
        Assert.Equal($"/v2/Services/{VerifySid}/Verifications", send.Path);
        Assert.Equal((Phone, "sms"), (send.Form["To"], send.Form["Channel"]));

        fake.Answers.Enqueue((HttpStatusCode.OK, """{"sid":"VE1","status":"pending"}"""));
        Assert.Equal(OtpCheck.Wrong, await otp.CheckAsync(Phone, "111111"));
        fake.Answers.Enqueue((HttpStatusCode.OK, """{"sid":"VE1","status":"approved"}"""));
        Assert.Equal(OtpCheck.Approved, await otp.CheckAsync(Phone, "123456"));
        Assert.Equal(("123456", $"/v2/Services/{VerifySid}/VerificationCheck"), (fake.Requests.Last().Form["Code"], fake.Requests.Last().Path));

        // Used or expired: Verify no longer has it.
        fake.Answers.Enqueue((HttpStatusCode.NotFound, """{"code":20404,"message":"The requested resource was not found"}"""));
        Assert.Equal(OtpCheck.ExpiredOrUsed, await otp.CheckAsync(Phone, "123456"));
        fake.Answers.Enqueue((HttpStatusCode.TooManyRequests, """{"code":60202,"message":"Max check attempts reached"}"""));
        Assert.Equal(OtpCheck.TooManyAttempts, await otp.CheckAsync(Phone, "123456"));

        Assert.DoesNotContain(logs.Lines, l => l.Contains("123456") || l.Contains("5550123") || l.Contains(Token));
    }

    [Fact]
    public async Task Without_Verify_The_App_Texts_Its_Own_Code_Once_Ten_Minutes_Five_Tries()
    {
        var clock = new Provider.TestClock();
        var (sp, fake, logs) = Create(new() { ["Twilio:VerifyServiceSid"] = null, ["Twilio:MessagingServiceSid"] = null, ["Twilio:FromNumber"] = FromNumber }, clock);
        using var __ = sp;
        var otp = sp.GetRequiredService<IOtpService>();
        Assert.IsType<TwilioMessagedOtpService>(otp);
        Assert.False(otp.IsStandIn);

        Assert.True((await otp.SendAsync(Phone)).Sent);
        var text = Assert.Single(fake.Requests);
        Assert.Equal($"/2010-04-01/Accounts/{AccountSid}/Messages.json", text.Path); // the Messages API, not Verify
        Assert.Equal((Phone, FromNumber), (text.Form["To"], text.Form["From"]));
        var code = System.Text.RegularExpressions.Regex.Match(text.Form["Body"], @"\b\d{6}\b").Value;
        Assert.Equal(6, code.Length);

        Assert.Equal(OtpCheck.Wrong, await otp.CheckAsync(Phone, code == "000000" ? "111111" : "000000"));
        Assert.Equal(OtpCheck.Approved, await otp.CheckAsync(Phone, code));
        Assert.Equal(OtpCheck.ExpiredOrUsed, await otp.CheckAsync(Phone, code)); // once only

        await otp.SendAsync(Phone);
        code = System.Text.RegularExpressions.Regex.Match(fake.Requests.Last().Form["Body"], @"\b\d{6}\b").Value;
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(OtpCheck.ExpiredOrUsed, await otp.CheckAsync(Phone, code)); // expired

        Assert.DoesNotContain(logs.Lines, l => l.Contains(code) || l.Contains("5550123") || l.Contains(Token));
    }

    [Fact]
    public async Task Without_Verify_A_Refused_Code_Text_Is_Not_Sent()
    {
        var (sp, fake, _) = Create(new() { ["Twilio:VerifyServiceSid"] = null });
        using var __ = sp;
        fake.Answers.Enqueue((HttpStatusCode.ServiceUnavailable, "<html>down</html>"));
        var result = await sp.GetRequiredService<IOtpService>().SendAsync(Phone);
        Assert.Equal((false, true), (result.Sent, result.Temporary));
    }

    [Fact]
    public async Task Verify_Out_Of_Reach_Is_Neither_Sent_Nor_Wrong()
    {
        var (sp, fake, _) = Create();
        using var __ = sp;
        fake.Unreachable = true;
        var otp = sp.GetRequiredService<IOtpService>();
        Assert.Equal((false, true), ((await otp.SendAsync(Phone)).Sent, (await otp.SendAsync(Phone)).Temporary));
        Assert.Equal(OtpCheck.Unavailable, await otp.CheckAsync(Phone, "123456"));
    }

    [Fact]
    public async Task The_Stand_In_Follows_Verifys_Rules_Once_Ten_Minutes_Five_Tries()
    {
        var folder = Path.Combine(Path.GetTempPath(), "inprem-otp-" + Guid.NewGuid().ToString("N"));
        var clock = new Provider.TestClock();
        var (sp, fake, logs) = Create(new() { ["Twilio:Mode"] = null, ["Twilio:StandInFolder"] = folder }, clock);
        using var __ = sp;
        try
        {
            var otp = sp.GetRequiredService<IOtpService>();
            Assert.True(otp.IsStandIn);

            await otp.SendAsync(Phone);
            var code = ReadCode(folder);
            Assert.Equal(OtpCheck.Wrong, await otp.CheckAsync(Phone, code == "000000" ? "111111" : "000000"));
            Assert.Equal(OtpCheck.Approved, await otp.CheckAsync(Phone, code));
            Assert.Equal(OtpCheck.ExpiredOrUsed, await otp.CheckAsync(Phone, code)); // P5-1: reuse rejected

            await otp.SendAsync(Phone);
            code = ReadCode(folder);
            clock.Advance(TimeSpan.FromMinutes(11));
            Assert.Equal(OtpCheck.ExpiredOrUsed, await otp.CheckAsync(Phone, code)); // P5-1: expiry rejected

            await otp.SendAsync("+12025550199");
            code = ReadCode(folder);
            var wrong = code == "000000" ? "111111" : "000000";
            for (var i = 0; i < 5; i++) Assert.Equal(OtpCheck.Wrong, await otp.CheckAsync("+12025550199", wrong));
            Assert.Equal(OtpCheck.TooManyAttempts, await otp.CheckAsync("+12025550199", code));

            Assert.Empty(fake.Requests); // nothing reached Twilio
            Assert.DoesNotContain(logs.Lines, l => l.Contains(code));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The code in the newest stand-in file ("Your Inprem Clock-in code is 123456.").</summary>
    private static string ReadCode(string folder)
    {
        var text = File.ReadAllText(Directory.GetFiles(folder, "*-code-*").OrderBy(f => f).Last());
        return System.Text.RegularExpressions.Regex.Match(text, @"code is (\d{6})").Groups[1].Value;
    }
}
