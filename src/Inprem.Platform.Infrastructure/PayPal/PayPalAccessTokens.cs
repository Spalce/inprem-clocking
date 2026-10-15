using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Inprem.Platform.Application.Billing;
using Microsoft.Extensions.Options;

namespace Inprem.Platform.Infrastructure.PayPal;

/// <summary>
/// PayPal OAuth access tokens (client credentials) for the typed HTTP calls, cached until shortly
/// before they expire. One instance per app; the SDK keeps its own token the same way.
/// </summary>
public sealed class PayPalAccessTokens
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly PayPalOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (string Token, DateTimeOffset Expires)? _current;

    public PayPalAccessTokens(IOptions<PayPalOptions> options, IHttpClientFactory httpFactory, TimeProvider time)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _time = time;
    }

    public async Task<string> GetAsync(CancellationToken ct = default)
    {
        if (_current is { } valid && valid.Expires > _time.GetUtcNow()) return valid.Token;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_current is { } again && again.Expires > _time.GetUtcNow()) return again.Token;

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("grant_type", "client_credentials") }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.Secret}")));
            using var response = await _httpFactory.CreateClient(PayPalPaymentProvider.HttpClientName).SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new PaymentProviderException($"PayPal refused the app's credentials (HTTP {(int)response.StatusCode}).", (int)response.StatusCode);

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var token = json?["access_token"]?.GetValue<string>() ?? throw new PaymentProviderException("PayPal returned no access token.");
            var seconds = json["expires_in"]?.GetValue<int>() ?? 0;
            var lifetime = TimeSpan.FromSeconds(seconds) - Margin;
            _current = (token, _time.GetUtcNow() + (lifetime > TimeSpan.Zero ? lifetime : TimeSpan.Zero));
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }
}
