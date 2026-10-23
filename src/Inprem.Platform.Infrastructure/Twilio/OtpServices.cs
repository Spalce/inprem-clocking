using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inprem.Platform.Infrastructure.Twilio;

/// <summary>
/// The stand-in for Twilio Verify (Twilio:Mode = StandIn): the same rules as Verify, kept in memory.
/// A 6-digit code works once, for 10 minutes, for 5 tries; a new code replaces the old one. Nothing
/// is texted: the code is saved in Twilio:StandInFolder (to read it while testing) and never logged.
/// </summary>
public sealed class StandInOtpService : IOtpService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxTries = 5;

    private sealed class Pending
    {
        public required byte[] CodeHash { get; init; }
        public required DateTime ExpiresUtc { get; init; }
        public int Tries { get; set; }
    }

    private readonly IMemoryCache _cache;
    private readonly IClock _clock;
    private readonly TwilioOptions _options;
    private readonly ILogger<StandInOtpService> _log;

    public StandInOtpService(IMemoryCache cache, IClock clock, IOptions<TwilioOptions> options, ILogger<StandInOtpService> log)
    {
        _cache = cache;
        _clock = clock;
        _options = options.Value;
        _log = log;
    }

    public bool IsStandIn => true;

    private static string Key(string phone) => "standin-otp:" + phone;

    public static string Text(string code) => $"Your Inprem Clock-in code is {code}. It works for 10 minutes.";

    /// <summary>A new code for the number (replacing any earlier one), kept only as a hash; the code itself is returned.</summary>
    public string Issue(string phoneE164)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _cache.Set(Key(phoneE164), new Pending { CodeHash = SHA256.HashData(Encoding.UTF8.GetBytes(code)), ExpiresUtc = _clock.UtcNow + Lifetime }, Lifetime + TimeSpan.FromMinutes(1));
        return code;
    }

    public async Task<OtpSendResult> SendAsync(string phoneE164, CancellationToken ct = default)
    {
        var code = Issue(phoneE164);
        if (!string.IsNullOrWhiteSpace(_options.StandInFolder))
        {
            Directory.CreateDirectory(_options.StandInFolder);
            var file = Path.Combine(_options.StandInFolder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-code-{phoneE164.TrimStart('+')}.txt");
            await File.WriteAllTextAsync(file, $"To: {phoneE164}\r\nSaved: {DateTime.UtcNow:u}\r\n\r\n{Text(code)}\r\n", Encoding.UTF8, ct).ConfigureAwait(false);
        }
        _log.LogInformation("Stand-in login code for {Phone}: not texted (Twilio Verify is not set up).", PhoneNumber.Mask(phoneE164));
        return OtpSendResult.Ok;
    }

    public Task<OtpCheck> CheckAsync(string phoneE164, string code, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(Key(phoneE164), out Pending? pending) || pending == null || pending.ExpiresUtc <= _clock.UtcNow)
            return Task.FromResult(OtpCheck.ExpiredOrUsed);
        lock (pending)
        {
            if (pending.Tries >= MaxTries) return Task.FromResult(OtpCheck.TooManyAttempts);
            if (!CryptographicOperations.FixedTimeEquals(pending.CodeHash, SHA256.HashData(Encoding.UTF8.GetBytes(code))))
            {
                pending.Tries++;
                return Task.FromResult(OtpCheck.Wrong);
            }
            _cache.Remove(Key(phoneE164)); // used: the same code never works twice
            return Task.FromResult(OtpCheck.Approved);
        }
    }
}

/// <summary>
/// Login codes without Twilio Verify (Twilio:Mode = Twilio, no Twilio:VerifyServiceSid): the app makes and checks the
/// code with the stand-in's rules (once, 10 minutes, 5 tries; kept in this instance's memory) and texts it through
/// Twilio's Messages API straight away, not through the outbox. The code is never logged.
/// </summary>
public sealed class TwilioMessagedOtpService : IOtpService
{
    private readonly StandInOtpService _codes;
    private readonly TwilioSmsSender _sms;

    public TwilioMessagedOtpService(StandInOtpService codes, TwilioSmsSender sms)
    {
        _codes = codes;
        _sms = sms;
    }

    public bool IsStandIn => false;

    public async Task<OtpSendResult> SendAsync(string phoneE164, CancellationToken ct = default)
    {
        var result = await _sms.SendAsync(phoneE164, StandInOtpService.Text(_codes.Issue(phoneE164)), ct).ConfigureAwait(false);
        return result.Accepted ? OtpSendResult.Ok : new OtpSendResult(false, result.Error ?? "Twilio refused the text.", Temporary: result.Temporary);
    }

    public Task<OtpCheck> CheckAsync(string phoneE164, string code, CancellationToken ct = default) => _codes.CheckAsync(phoneE164, code, ct);
}

/// <summary>
/// Twilio Verify (Twilio:Mode = Twilio, Twilio:VerifyServiceSid): POST /v2/Services/{sid}/Verifications
/// sends a code by text; POST …/VerificationCheck checks it. Verify keeps the code: it expires after
/// 10 minutes and is gone once approved, so a reused or expired code finds nothing (404).
/// </summary>
public sealed class TwilioVerifyOtpService : IOtpService
{
    public const string HttpClientName = "twilio-verify";

    private readonly IHttpClientFactory _http;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioVerifyOtpService> _log;

    public TwilioVerifyOtpService(IHttpClientFactory http, IOptions<TwilioOptions> options, ILogger<TwilioVerifyOtpService> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public bool IsStandIn => false;

    public async Task<OtpSendResult> SendAsync(string phoneE164, CancellationToken ct = default)
    {
        var (status, json) = await PostAsync("Verifications", new() { ["To"] = phoneE164, ["Channel"] = "sms" }, ct).ConfigureAwait(false);
        if (status is HttpStatusCode.Created or HttpStatusCode.OK) return OtpSendResult.Ok;
        var code = Read(json, "code");
        _log.LogWarning("Twilio Verify did not send a code to {Phone}: HTTP {Status}, error {Code}.", PhoneNumber.Mask(phoneE164), status == 0 ? "none" : ((int)status).ToString(), code ?? "(none)");
        return new OtpSendResult(false, $"Twilio Verify error {code ?? (status == 0 ? "unreachable" : ((int)status).ToString())}", Temporary: status == 0 || (int)status >= 500 || status == HttpStatusCode.TooManyRequests);
    }

    public async Task<OtpCheck> CheckAsync(string phoneE164, string code, CancellationToken ct = default)
    {
        var (status, json) = await PostAsync("VerificationCheck", new() { ["To"] = phoneE164, ["Code"] = code }, ct).ConfigureAwait(false);
        if (status == HttpStatusCode.OK) return Read(json, "status") == "approved" ? OtpCheck.Approved : OtpCheck.Wrong;
        if (status == HttpStatusCode.NotFound) return OtpCheck.ExpiredOrUsed;
        if (status == HttpStatusCode.TooManyRequests || Read(json, "code") == "60202") return OtpCheck.TooManyAttempts;
        _log.LogWarning("Twilio Verify could not check a code for {Phone}: HTTP {Status}.", PhoneNumber.Mask(phoneE164), status == 0 ? "none" : ((int)status).ToString());
        return OtpCheck.Unavailable;
    }

    /// <summary>Status 0 when Twilio could not be reached.</summary>
    private async Task<(HttpStatusCode Status, string Json)> PostAsync(string action, Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/Services/{Uri.EscapeDataString(_options.VerifyServiceSid!)}/{action}") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _options.BasicCredentials());
        try
        {
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct).ConfigureAwait(false);
            return (response.StatusCode, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (0, "");
        }
    }

    private static string? Read(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null ? value.ToString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
