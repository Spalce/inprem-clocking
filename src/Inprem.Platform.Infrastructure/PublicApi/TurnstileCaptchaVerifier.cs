using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Inprem.Platform.Application.PublicApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inprem.Platform.Infrastructure.PublicApi;

/// <summary>Configuration section <c>Captcha</c>: the Turnstile secret from user-secrets or the host (never the repository).</summary>
public sealed class CaptchaOptions
{
    public const string Section = "Captcha";

    public string? Secret { get; set; }
}

/// <summary>
/// Cloudflare Turnstile (D30): the server asks Cloudflare whether the widget's answer is good.
/// Fails closed: without a secret, or when Cloudflare cannot be reached, the answer is "no".
/// </summary>
public sealed class TurnstileCaptchaVerifier : ICaptchaVerifier
{
    public const string HttpClientName = "Turnstile";
    private static readonly Uri SiteVerify = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    private readonly IHttpClientFactory _http;
    private readonly CaptchaOptions _options;
    private readonly ILogger<TurnstileCaptchaVerifier> _log;

    public TurnstileCaptchaVerifier(IHttpClientFactory http, IOptions<CaptchaOptions> options, ILogger<TurnstileCaptchaVerifier> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        if (string.IsNullOrWhiteSpace(_options.Secret))
        {
            _log.LogWarning("CAPTCHA refused: Captcha:Secret is not configured.");
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = _options.Secret, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(remoteIp)) form["remoteip"] = remoteIp;
        try
        {
            using var response = await _http.CreateClient(HttpClientName).PostAsync(SiteVerify, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            var answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken: ct).ConfigureAwait(false);
            return answer?.Success == true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _log.LogWarning("CAPTCHA refused: Cloudflare could not be asked ({Error}).", e.GetType().Name);
            return false;
        }
    }

    private sealed record Answer([property: JsonPropertyName("success")] bool Success);
}
