using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inprem.Platform.Application.Billing;

namespace Inprem.Platform.Infrastructure.PayPal;

/// <summary>
/// PayPal's REST calls that the SDK does not cover (catalog products, webhook verification,
/// Invoicing v2, refunds), on the shared <c>PayPal</c> HttpClient. Errors become a
/// <see cref="PaymentProviderException"/> with PayPal's status, debug id and error code, never a payload.
/// </summary>
internal sealed class PayPalRest
{
    /// <summary>
    /// Set on a request PayPal cannot make safe to repeat (no PayPal-Request-Id support): the
    /// resilience pipeline then never retries it (<see cref="PayPalHosting"/>).
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> NoRetry = new("Inprem.PayPal.NoRetry");

    private readonly HttpClient _http;
    private readonly PayPalAccessTokens _tokens;

    public PayPalRest(HttpClient http, PayPalAccessTokens tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, string? requestId, CancellationToken ct, bool retry = true, string? prefer = null)
    {
        try
        {
            var token = await _tokens.GetAsync(ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (requestId != null) request.Headers.Add("PayPal-Request-Id", requestId);
            if (prefer != null) request.Headers.Add("Prefer", prefer);
            if (!retry) request.Options.Set(NoRetry, true);
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            throw new PaymentProviderException($"PayPal did not answer ({path}).", null, null, e);
        }
    }

    /// <summary>The JSON answer of a successful call (an empty object for an empty body), else a <see cref="PaymentProviderException"/>.</summary>
    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new PaymentProviderException($"PayPal could not {what} (HTTP {(int)response.StatusCode}).", (int)response.StatusCode, DebugId(response), null, Issue(text));
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException e)
        {
            throw new PaymentProviderException($"PayPal answered with something that is not JSON ({what}).", (int)response.StatusCode, DebugId(response), e);
        }
    }

    public static string? DebugId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("PayPal-Debug-Id", out var values) ? values.FirstOrDefault() : null;

    /// <summary>PayPal's error code only (<c>details[0].issue</c>, else <c>name</c>): codes are upper-case words, never personal data.</summary>
    private static string? Issue(string text)
    {
        try
        {
            var error = JsonNode.Parse(text);
            var issue = (error?["details"] as JsonArray)?.FirstOrDefault()?["issue"]?.GetValue<string>() ?? error?["name"]?.GetValue<string>();
            return issue != null && issue.Length <= 100 && issue.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ? issue : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
