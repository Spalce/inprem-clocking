using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaypalServerSdk.Standard;
using PaypalServerSdk.Standard.Authentication;
using PaypalServerSdk.Standard.Exceptions;
using Sdk = PaypalServerSdk.Standard.Models;

namespace Inprem.Platform.Infrastructure.PayPal;

/// <summary>
/// PayPal through PayPalServerSDK (plans and subscriptions) and a typed HTTP client (catalog
/// products and webhook verification, which the SDK does not cover). Both use the same
/// <see cref="HttpClient"/>, so the same timeout, retries (<see cref="DependencyInjection"/>) and
/// logging apply. Payloads are never logged: they hold payer names and emails.
/// </summary>
public sealed class PayPalPaymentProvider : IPaymentProvider
{
    /// <summary>D23: PayPal suspends a subscription after one failed payment cycle (after its own retries).</summary>
    public const int PaymentFailureThreshold = 1;

    public const string HttpClientName = "PayPal";

    private readonly PayPalOptions _options;
    private readonly HttpClient _http;
    private readonly PayPalAccessTokens _tokens;
    private readonly ILogger<PayPalPaymentProvider> _log;
    private readonly Lazy<PaypalServerSdkClient> _sdk;
    private readonly PayPalRest _rest;

    public PayPalPaymentProvider(IOptions<PayPalOptions> options, IHttpClientFactory httpFactory, PayPalAccessTokens tokens, ILogger<PayPalPaymentProvider> log)
    {
        _options = options.Value;
        _http = httpFactory.CreateClient(HttpClientName);
        _tokens = tokens;
        _log = log;
        _rest = new PayPalRest(_http, tokens);
        _sdk = new Lazy<PaypalServerSdkClient>(() => new PaypalServerSdkClient.Builder()
            .Environment(_options.Mode == PayPalMode.Live ? PaypalServerSdk.Standard.Environment.Production : PaypalServerSdk.Standard.Environment.Sandbox)
            .ClientCredentialsAuth(new ClientCredentialsAuthModel.Builder(_options.ClientId!, _options.Secret!).Build())
            .HttpClientConfig(c => c.HttpClientInstance(_http, false).NumberOfRetries(0).Timeout(TimeSpan.FromSeconds(_options.TimeoutSeconds)))
            .Build());
    }

    public bool IsConfigured => _options.IsConfigured;

    public bool IsAllowedReturnUrl(string url) => _options.IsAllowedReturnUrl(url);

    // ------------------------------------------------------------------ catalog

    public async Task<string> CreateProductAsync(string name, string? description, string requestId, CancellationToken ct = default)
    {
        RequireConfigured();
        var body = new JsonObject { ["name"] = Cut(name, 127), ["type"] = "SERVICE", ["category"] = "SOFTWARE" };
        if (!string.IsNullOrWhiteSpace(description)) body["description"] = Cut(description, 256);
        using var response = await SendAsync(HttpMethod.Post, "v1/catalogs/products", body, requestId, ct).ConfigureAwait(false);
        return (await ReadAsync(response, "create a product", ct).ConfigureAwait(false))["id"]?.GetValue<string>()
            ?? throw new PaymentProviderException("PayPal created a product without an id.", (int)response.StatusCode, DebugId(response));
    }

    public async Task<string> CreatePlanAsync(NewBillingPlan plan, string requestId, CancellationToken ct = default)
    {
        RequireConfigured();
        if (_options.PlanReturnUrl == null)
            throw new PaymentProviderException("PayPal:PlanReturnUrl is not set: every PayPal plan carries our return page as a fallback (docs/multitenancy/paypal-notes.md).");
        var request = new Sdk.PlanRequest
        {
            ProductId = plan.ProductId,
            Name = Cut(plan.Name, 127),
            Description = plan.Description == null ? null : Cut(plan.Description, 127),
            Status = Sdk.PlanRequestStatus.Active,
            BillingCycles = new List<Sdk.SubscriptionBillingCycle>
            {
                new()
                {
                    Frequency = new Sdk.Frequency { IntervalUnit = plan.Cycle == BillingCycle.Yearly ? Sdk.IntervalUnit.Year : Sdk.IntervalUnit.Month, IntervalCount = 1 },
                    TenureType = Sdk.TenureType.Regular,
                    Sequence = 1,
                    TotalCycles = 0, // renews until cancelled
                    PricingScheme = new Sdk.SubscriptionPricingScheme { FixedPrice = Money(plan.Amount, plan.Currency) },
                },
            },
            PaymentPreferences = new Sdk.PaymentPreferences
            {
                AutoBillOutstanding = true,
                PaymentFailureThreshold = PaymentFailureThreshold,
                SetupFeeFailureAction = Sdk.SetupFeeFailureAction.Cancel,
            },
            // Fallback (developer's choice, step 2.2): if PayPal ever stops honouring the deprecated
            // application_context addresses of a subscription, the buyer still comes back to our app.
            MerchantPreferences = new Sdk.MerchantPreferences { ReturnUrl = _options.PlanReturnUrl, CancelUrl = _options.PlanCancelUrl ?? _options.PlanReturnUrl },
        };

        var created = await CallAsync("create a billing plan", () => _sdk.Value.SubscriptionsController.CreateBillingPlanAsync(
            new Sdk.CreateBillingPlanInput { Body = request, PaypalRequestId = requestId, Prefer = "return=minimal" }, ct)).ConfigureAwait(false);
        return created.Data?.Id ?? throw new PaymentProviderException("PayPal created a plan without an id.", created.StatusCode);
    }

    // ------------------------------------------------------------ subscriptions

    public async Task<CreatedSubscription> CreateSubscriptionAsync(string planId, string customId, string returnUrl, string cancelUrl, string requestId, CancellationToken ct = default)
    {
        RequireConfigured();
        RequireAllowed(returnUrl, nameof(returnUrl));
        RequireAllowed(cancelUrl, nameof(cancelUrl));

        // application_context is marked deprecated by PayPal without a replacement (docs/multitenancy/paypal-notes.md);
        // it is still the only per-subscription way to set the addresses, so it is sent, with the plan's as a fallback.
#pragma warning disable CS0618
        var request = new Sdk.CreateSubscriptionRequest
        {
            PlanId = planId,
            CustomId = customId,
            ApplicationContext = new Sdk.SubscriptionApplicationContext
            {
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl,
                UserAction = Sdk.ApplicationContextUserAction.SubscribeNow,
                ShippingPreference = Sdk.ExperienceContextShippingPreference.NoShipping,
            },
        };
#pragma warning restore CS0618

        var created = await CallAsync("create a subscription", () => _sdk.Value.SubscriptionsController.CreateSubscriptionAsync(
            new Sdk.CreateSubscriptionInput { Body = request, PaypalRequestId = requestId, Prefer = "return=representation" }, ct)).ConfigureAwait(false);
        var subscription = created.Data ?? throw new PaymentProviderException("PayPal returned no subscription.", created.StatusCode);
        var approve = subscription.Links?.FirstOrDefault(l => string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase))?.Href;
        if (string.IsNullOrWhiteSpace(subscription.Id) || string.IsNullOrWhiteSpace(approve))
            throw new PaymentProviderException("PayPal returned a subscription without an id or approve link.", created.StatusCode);
        return new CreatedSubscription(subscription.Id, PayPalStatus.Parse(subscription.Status?.ToString()), approve);
    }

    public async Task<PayPalSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        RequireConfigured();
        try
        {
            var response = await CallAsync("read a subscription", () => _sdk.Value.SubscriptionsController.GetSubscriptionAsync(
                new Sdk.GetSubscriptionInput { Id = subscriptionId }, ct)).ConfigureAwait(false);
            return response.Data == null ? null : Map(response.Data);
        }
        catch (PaymentProviderException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string?> ReviseSubscriptionAsync(string subscriptionId, string planId, string returnUrl, string cancelUrl, CancellationToken ct = default)
    {
        RequireConfigured();
        RequireAllowed(returnUrl, nameof(returnUrl));
        RequireAllowed(cancelUrl, nameof(cancelUrl));
        var revised = await CallAsync("change a subscription's plan", () => _sdk.Value.SubscriptionsController.ReviseSubscriptionAsync(
            new Sdk.ReviseSubscriptionInput
            {
                Id = subscriptionId,
                Body = new Sdk.ModifySubscriptionRequest
                {
                    PlanId = planId,
                    ApplicationContext = new Sdk.SubscriptionPatchApplicationContext { ReturnUrl = returnUrl, CancelUrl = cancelUrl },
                },
            }, ct)).ConfigureAwait(false);
        return revised.Data?.Links?.FirstOrDefault(l => string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase))?.Href;
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, string reason, CancellationToken ct = default)
    {
        RequireConfigured();
        try
        {
            await CallAsync("cancel a subscription", async () =>
            {
                await _sdk.Value.SubscriptionsController.CancelSubscriptionAsync(
                    new Sdk.CancelSubscriptionInput { Id = subscriptionId, Body = new Sdk.CancelSubscriptionRequest { Reason = Cut(string.IsNullOrWhiteSpace(reason) ? "Cancelled" : reason, 127) } }, ct).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        catch (PaymentProviderException e) when (e.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // PayPal answers 422 for a subscription that is already cancelled or expired: re-read to be sure.
            var now = await GetSubscriptionAsync(subscriptionId, ct).ConfigureAwait(false);
            if (now?.State is not (PayPalSubscriptionState.Cancelled or PayPalSubscriptionState.Expired)) throw;
        }
    }

    // ----------------------------------------------------------------- webhooks

    private static readonly (string Header, string Field)[] WebhookHeaders =
    {
        ("PAYPAL-AUTH-ALGO", "auth_algo"), ("PAYPAL-CERT-URL", "cert_url"), ("PAYPAL-TRANSMISSION-ID", "transmission_id"),
        ("PAYPAL-TRANSMISSION-SIG", "transmission_sig"), ("PAYPAL-TRANSMISSION-TIME", "transmission_time"),
    };

    public async Task<bool> VerifyWebhookAsync(WebhookMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(_options.WebhookId))
        {
            _log.LogWarning("PayPal webhook refused: PayPal or PayPal:WebhookId is not configured.");
            return false;
        }

        var headers = new Dictionary<string, string>(message.Headers, StringComparer.OrdinalIgnoreCase);
        var body = new JsonObject { ["webhook_id"] = _options.WebhookId };
        foreach (var (header, field) in WebhookHeaders)
        {
            if (!headers.TryGetValue(header, out var value) || string.IsNullOrWhiteSpace(value))
            {
                _log.LogWarning("PayPal webhook refused: header {Header} missing.", header);
                return false;
            }
            body[field] = value;
        }

        try
        {
            body["webhook_event"] = JsonNode.Parse(message.Body);
        }
        catch (JsonException)
        {
            _log.LogWarning("PayPal webhook refused: the body is not JSON.");
            return false;
        }

        try
        {
            using var response = await SendAsync(HttpMethod.Post, "v1/notifications/verify-webhook-signature", body, null, ct).ConfigureAwait(false);
            var status = (await ReadAsync(response, "verify a webhook", ct).ConfigureAwait(false))["verification_status"]?.GetValue<string>();
            if (status == "SUCCESS") return true;
            _log.LogWarning("PayPal webhook refused: verification status {Status}.", status ?? "(none)");
            return false;
        }
        catch (PaymentProviderException e)
        {
            // PayPal could not be asked: refuse (PayPal retries the delivery later).
            _log.LogWarning("PayPal webhook not verified: PayPal call failed ({Status}, debug id {DebugId}).", e.StatusCode, e.DebugId);
            return false;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static PayPalSubscription Map(Sdk.Subscription s)
    {
        var billing = s.BillingInfo;
        return new PayPalSubscription(
            s.Id ?? "",
            PayPalStatus.Parse(s.Status?.ToString()),
            s.Status?.ToString(),
            s.PlanId,
            s.CustomId,
            Time(s.StartTime),
            Time(billing?.NextBillingTime),
            Time(billing?.LastPayment?.Time),
            Amount(billing?.LastPayment?.Amount?.MValue),
            billing?.LastPayment?.Amount?.CurrencyCode,
            Amount(billing?.OutstandingBalance?.MValue) ?? 0m,
            billing?.FailedPaymentsCount ?? 0);
    }

    private static Sdk.Money Money(decimal amount, string currency) =>
        new() { CurrencyCode = currency, MValue = amount.ToString("0.00", CultureInfo.InvariantCulture) };

    private static decimal? Amount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static DateTime? Time(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time.UtcDateTime : null;

    private static string Cut(string value, int max) => value.Length > max ? value[..max] : value;

    private void RequireConfigured()
    {
        if (!IsConfigured) throw new PaymentProviderException("PayPal is not configured (PayPal:ClientId and PayPal:Secret).");
    }

    private void RequireAllowed(string url, string name)
    {
        if (!_options.IsAllowedReturnUrl(url)) throw new ArgumentException("The address is not an https address on an allowed origin (PayPal:ReturnUrlAllowList).", name);
    }

    /// <summary>Runs an SDK call; SDK and network errors become a <see cref="PaymentProviderException"/> (no payloads).</summary>
    private async Task<T> CallAsync<T>(string what, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ApiException e)
        {
            var debugId = e.HttpContext?.Response?.Headers?.TryGetValue("PayPal-Debug-Id", out var id) == true ? id : null;
            _log.LogWarning("PayPal could not {What}: HTTP {Status}, debug id {DebugId}.", what, e.ResponseCode, debugId);
            throw new PaymentProviderException($"PayPal could not {what} (HTTP {e.ResponseCode}).", e.ResponseCode, debugId, e);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _log.LogWarning("PayPal could not {What}: {Error}.", what, e.GetType().Name);
            throw new PaymentProviderException($"PayPal could not {what}: no answer.", null, null, e);
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode body, string? requestId, CancellationToken ct) =>
        _rest.SendAsync(method, path, body, requestId, ct);

    private static Task<JsonNode> ReadAsync(HttpResponseMessage response, string what, CancellationToken ct) => PayPalRest.ReadAsync(response, what, ct);

    private static string? DebugId(HttpResponseMessage response) => PayPalRest.DebugId(response);
}
