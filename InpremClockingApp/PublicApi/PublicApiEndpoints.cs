using System.Globalization;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Organizations;
using InpremClockingApp.Provider.Organizations;
using Microsoft.Extensions.Caching.Memory;

namespace InpremClockingApp.PublicApi;

/// <summary>
/// The website API's endpoints (Phase 3). Public endpoints need no credential and return no
/// personal data (design 7.2).
/// </summary>
public static class PublicApiEndpoints
{
    public static readonly TimeSpan PlansCacheTime = TimeSpan.FromMinutes(5);
    private const string PlansCacheKey = "public-api:plans";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/health", () => Results.Ok(new HealthResponse("ok")))
            .AllowAnonymous()
            .WithName("getHealth")
            .WithTags("Public")
            .Produces<HealthResponse>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/plans", GetPlansAsync)
            .AllowAnonymous()
            .WithName("listPlans")
            .WithTags("Public")
            .Produces<IReadOnlyList<PlanResponse>>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/timezones", () => Results.Ok(OrganizationUi.TimeZones.Select(z => new TimeZoneResponse(z.Id, z.Label)).ToList()))
            .AllowAnonymous()
            .WithName("listTimeZones")
            .WithTags("Public")
            .Produces<IReadOnlyList<TimeZoneResponse>>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Registration (step 3.3).
        api.MapPost("/signups", CreateSignupAsync)
            .AllowAnonymous()
            .RequireRateLimiting(PublicApiHosting.WriteRatePolicy)
            .RequireIdempotency()
            .WithName("createSignup")
            .WithTags("Signup")
            .Accepts<SignupRequest>("application/json")
            .Produces<SignupCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/signups/{signupId:guid}", GetSignupAsync)
            .RequireAuthorization(PublicApiHosting.SignupPolicy)
            .WithName("getSignup")
            .WithTags("Signup")
            .Produces<SignupStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapPost("/signups/{signupId:guid}/resend-verification", ResendVerificationAsync)
            .RequireAuthorization(PublicApiHosting.SignupPolicy)
            .WithName("resendVerification")
            .WithTags("Signup")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Checkout (step 3.4): the Phase 2 checkout, for the signup's or the billing token's organization.
        api.MapPost("/checkout-sessions", CreateCheckoutAsync)
            .RequireAuthorization(PublicApiHosting.AnyTokenPolicy)
            .RequireIdempotency()
            .WithName("createCheckoutSession")
            .WithTags("Checkout")
            .Accepts<CheckoutRequest>("application/json")
            .Produces<CheckoutCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        api.MapGet("/checkout-sessions/{checkoutSessionId:guid}", (HttpContext http, Guid checkoutSessionId, CheckoutService checkout, ICheckoutStore store) =>
                CheckoutActionAsync(http, checkoutSessionId, checkout, store, null))
            .RequireAuthorization(PublicApiHosting.AnyTokenPolicy)
            .WithName("getCheckoutSession")
            .WithTags("Checkout")
            .Produces<CheckoutStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapPost("/checkout-sessions/{checkoutSessionId:guid}/confirm", (HttpContext http, Guid checkoutSessionId, CheckoutService checkout, ICheckoutStore store) =>
                CheckoutActionAsync(http, checkoutSessionId, checkout, store, (s, ct) => checkout.ConfirmAsync(s.Id, CheckoutSource.Confirm, ct)))
            .RequireAuthorization(PublicApiHosting.AnyTokenPolicy)
            .WithName("confirmCheckoutSession")
            .WithTags("Checkout")
            .Produces<CheckoutStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapPost("/checkout-sessions/{checkoutSessionId:guid}/cancel", (HttpContext http, Guid checkoutSessionId, CheckoutService checkout, ICheckoutStore store) =>
                CheckoutActionAsync(http, checkoutSessionId, checkout, store, (s, ct) => checkout.CancelByBuyerAsync(s.Id, ct)))
            .RequireAuthorization(PublicApiHosting.AnyTokenPolicy)
            .WithName("cancelCheckoutSession")
            .WithTags("Checkout")
            .Produces<CheckoutStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Existing customers (step 3.5).
        api.MapPost("/billing-access/requests", RequestBillingAccessAsync)
            .AllowAnonymous()
            .RequireRateLimiting(PublicApiHosting.WriteRatePolicy)
            .WithName("requestBillingAccess")
            .WithTags("Billing access")
            .Accepts<BillingAccessRequest>("application/json")
            .Produces<AcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapPost("/billing-access/sessions", CreateBillingAccessSessionAsync)
            .AllowAnonymous()
            .RequireRateLimiting(PublicApiHosting.WriteRatePolicy)
            .WithName("createBillingAccessSession")
            .WithTags("Billing access")
            .Accepts<BillingAccessSessionRequest>("application/json")
            .Produces<BillingAccessSessionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/account/subscription", GetAccountSubscriptionAsync)
            .RequireAuthorization(PublicApiHosting.BillingPolicy)
            .WithName("getAccountSubscription")
            .WithTags("Account")
            .Produces<AccountSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/account/invoices", GetAccountInvoicesAsync)
            .RequireAuthorization(PublicApiHosting.BillingPolicy)
            .WithName("listAccountInvoices")
            .WithTags("Account")
            .Produces<IReadOnlyList<InvoiceResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapPost("/account/subscription/cancel", CancelAccountSubscriptionAsync)
            .RequireAuthorization(PublicApiHosting.BillingPolicy)
            .WithName("cancelAccountSubscription")
            .WithTags("Account")
            .Produces<CancellationScheduledResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        api.MapPost("/account/subscription/keep", (HttpContext http) =>
                // A PayPal cancellation is final (PayPal cannot reactivate a cancelled subscription): to stay, subscribe again.
                Problems.Create(http, StatusCodes.Status409Conflict, Problems.CannotKeep, "A cancellation cannot be withdrawn. To stay, subscribe again (checkout with purpose resubscribe)."))
            .RequireAuthorization(PublicApiHosting.BillingPolicy)
            .WithName("keepAccountSubscription")
            .WithTags("Account")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapGet("/slugs/{slug}/availability", GetSlugAvailabilityAsync)
            .AllowAnonymous()
            .WithName("getSlugAvailability")
            .WithTags("Public")
            .Produces<SlugAvailabilityResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// The plans on sale: public, offered plans with their active prices. Cached for 5 minutes, here
    /// and by the browser (<c>Cache-Control: public, max-age=300</c>).
    /// </summary>
    private static async Task<IResult> GetPlansAsync(HttpContext http, PlanService plans, IMemoryCache cache)
    {
        var list = await cache.GetOrCreateAsync(PlansCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlansCacheTime;
            return (await plans.ListOfferedAsync(http.RequestAborted))
                .Where(p => p.IsPublic)
                .OrderBy(p => p.Prices.Where(x => x.Status == PlanStatus.Active).Min(x => x.Amount))
                .Select(ToResponse)
                .ToList();
        });
        http.Response.Headers.CacheControl = $"public, max-age={(int)PlansCacheTime.TotalSeconds}";
        return Results.Ok(list);
    }

    public static PlanResponse ToResponse(Plan plan) => new(
        plan.Id,
        plan.Name,
        plan.Description,
        plan.TrialDays,
        (plan.Features ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        new PlanLimits(plan.MaxPeople),
        plan.Prices.Where(p => p.Status == PlanStatus.Active).OrderBy(p => p.Cycle).Select(ToResponse).ToList());

    public static PlanPriceResponse ToResponse(PlanPrice price) => new(price.Id, price.Cycle.ToString(), Money(price.Amount, price.Currency));

    public static MoneyResponse Money(decimal amount, string currency) => new(amount.ToString("0.00", CultureInfo.InvariantCulture), currency);

    /// <summary>Is the organization address name free? A badly formed name is a 400; a taken or reserved one comes with a free suggestion.</summary>
    private static async Task<IResult> GetSlugAvailabilityAsync(HttpContext http, string slug, OrganizationService organizations)
    {
        var value = (slug ?? "").Trim().ToLowerInvariant();
        var problem = TenantProfile.SlugProblem(value);
        if (problem != null && !TenantProfile.Reserved.Contains(value))
            return Problems.Validation(http, new Dictionary<string, string[]> { ["slug"] = new[] { problem } });

        var check = await organizations.CheckSlugAsync(value, ct: http.RequestAborted);
        return Results.Ok(new SlugAvailabilityResponse(value, check.Available, check.Available ? null : check.Suggestion));
    }

    // ------------------------------------------------------------- registration

    private static string AppBase(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";

    private static async Task<IResult> CreateSignupAsync(HttpContext http, SignupRequest? body, Inprem.Platform.Application.PublicApi.SignupService signups)
    {
        if (body == null) return Problems.Validation(http, new Dictionary<string, string[]> { ["body"] = new[] { "A JSON body is required." } });
        var result = await signups.CreateAsync(new Inprem.Platform.Application.PublicApi.SignupInput(
            body.OrganizationName, body.Slug, body.TimeZone, body.Owner?.FirstName, body.Owner?.LastName, body.Owner?.Email, body.Owner?.Mobile,
            body.Consent?.AcceptTerms == true, body.Consent?.AcceptPrivacyPolicy == true, body.Consent?.SmsOptIn == true,
            body.PlanPriceId, body.Mode, body.CaptchaToken, http.Connection.RemoteIpAddress?.ToString()), AppBase(http), http.RequestAborted);

        if (result.Error is { } error)
        {
            return error switch
            {
                Inprem.Platform.Application.PublicApi.SignupError.Validation => Problems.Validation(http, result.Errors!),
                Inprem.Platform.Application.PublicApi.SignupError.Captcha => Problems.Create(http, StatusCodes.Status400BadRequest, Problems.CaptchaFailed, result.Message!),
                Inprem.Platform.Application.PublicApi.SignupError.PlanNotAvailable => Problems.Create(http, StatusCodes.Status400BadRequest, Problems.PlanNotAvailable, result.Message!),
                Inprem.Platform.Application.PublicApi.SignupError.SlugTaken => Problems.Create(http, StatusCodes.Status409Conflict, Problems.SlugTaken, result.Message!),
                Inprem.Platform.Application.PublicApi.SignupError.EmailInUse => Problems.Create(http, StatusCodes.Status409Conflict, Problems.EmailInUse, result.Message!),
                _ => Problems.Create(http, StatusCodes.Status500InternalServerError, Problems.InternalError, result.Message!),
            };
        }

        var signup = result.Signup!;
        return Results.Created($"{PublicApiHosting.Prefix}/signups/{signup.Id}", new SignupCreatedResponse(
            signup.Id, result.Token!, result.TokenExpiresUtc!.Value, "PendingVerification",
            signup.Mode == Inprem.Platform.Domain.PublicApi.SignupMode.Subscribe ? "checkout" : "verify_email"));
    }

    /// <summary>The signup token's own signup only; anything else is "not found".</summary>
    private static bool OwnSignup(HttpContext http, Guid signupId) => PublicApiClaims.SignupOf(http.User) == signupId;

    private static async Task<IResult> GetSignupAsync(HttpContext http, Guid signupId, Inprem.Platform.Application.PublicApi.SignupService signups)
    {
        var view = OwnSignup(http, signupId) ? await signups.GetAsync(signupId, http.RequestAborted) : null;
        return view == null
            ? Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No such signup.")
            : Results.Ok(new SignupStatusResponse(view.SignupId, view.Status, view.SubscriptionStatus?.ToString()));
    }

    private static async Task<IResult> ResendVerificationAsync(HttpContext http, Guid signupId, Inprem.Platform.Application.PublicApi.SignupService signups)
    {
        if (!OwnSignup(http, signupId)) return Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No such signup.");
        return await signups.ResendAsync(signupId, AppBase(http), http.RequestAborted) switch
        {
            Inprem.Platform.Application.PublicApi.ResendOutcome.Sent => Results.Accepted(),
            Inprem.Platform.Application.PublicApi.ResendOutcome.AlreadyVerified => Problems.Create(http, StatusCodes.Status409Conflict, Problems.AlreadyVerified, "The email is already verified."),
            Inprem.Platform.Application.PublicApi.ResendOutcome.LimitReached => Problems.Create(http, StatusCodes.Status409Conflict, Problems.ResendLimitReached, "At most 3 verification emails per hour. Please wait."),
            _ => Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No such signup."),
        };
    }

    // ----------------------------------------------------------------- checkout

    private static readonly Dictionary<string, CheckoutPurpose> Purposes = new()
    {
        ["new"] = CheckoutPurpose.New, ["convert"] = CheckoutPurpose.Convert, ["resubscribe"] = CheckoutPurpose.Resubscribe, ["change_plan"] = CheckoutPurpose.ChangePlan,
    };

    /// <summary>Who started a checkout from the website: the token (only that token may see it later).</summary>
    private static string TokenActor(HttpContext http) => $"token:{PublicApiClaims.TokenIdOf(http.User):N}";

    private static async Task<IResult> CreateCheckoutAsync(HttpContext http, CheckoutRequest? body, CheckoutService checkout)
    {
        var errors = new Dictionary<string, string[]>();
        if (body?.PlanPriceId is null || body.PlanPriceId == Guid.Empty) errors["planPriceId"] = new[] { "Choose a plan price from GET /plans." };
        if (body?.Purpose == null || !Purposes.TryGetValue(body.Purpose, out var purpose)) { errors["purpose"] = new[] { "Use new, convert, resubscribe or change_plan." }; purpose = default; }
        if (string.IsNullOrWhiteSpace(body?.ReturnUrl)) errors["returnUrl"] = new[] { "The return address is required." };
        if (string.IsNullOrWhiteSpace(body?.CancelUrl)) errors["cancelUrl"] = new[] { "The cancel address is required." };
        if (errors.Count > 0) return Problems.Validation(http, errors);

        // A signup token checks out as "new"; a billing token as convert, resubscribe or change_plan.
        var signupToken = http.User.FindFirst(PublicApiClaims.Kind)?.Value == nameof(Inprem.Platform.Domain.PublicApi.ApiTokenKind.Signup);
        if (signupToken != (purpose == CheckoutPurpose.New))
            return Problems.Validation(http, new Dictionary<string, string[]> { ["purpose"] = new[] { signupToken ? "A signup token checks out as new." : "Use convert, resubscribe or change_plan." } });
        if (PublicApiClaims.TenantOf(http.User) is not int tenantId) return Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No organization for this token.");

        var key = $"{PublicApiClaims.TokenIdOf(http.User):N}:{http.Request.Headers[IdempotencyFilter.Header].ToString().Trim()}";
        var result = await checkout.StartAsync(new StartCheckout(purpose, tenantId, body!.PlanPriceId!.Value, body.ReturnUrl!, body.CancelUrl!, key, TokenActor(http),
            signupToken ? PublicApiClaims.SignupOf(http.User) : null), http.RequestAborted);
        if (result.Succeeded && result.Session?.ApprovalUrl != null)
        {
            var s = result.Session;
            return Results.Created($"{PublicApiHosting.Prefix}/checkout-sessions/{s.Id}", new CheckoutCreatedResponse(s.Id, s.ApprovalUrl, s.ExpiresUtc));
        }

        return result.Failure switch
        {
            CheckoutFailure.ReturnUrlNotAllowed => Problems.Create(http, StatusCodes.Status400BadRequest, Problems.ReturnUrlNotAllowed, "The return or cancel address is not on an allowed website."),
            CheckoutFailure.PlanNotAvailable => Problems.Create(http, StatusCodes.Status400BadRequest, Problems.PlanNotAvailable, result.Error!),
            CheckoutFailure.WrongPurpose => Problems.Validation(http, new Dictionary<string, string[]> { ["purpose"] = new[] { result.Error! } }),
            CheckoutFailure.KeyReused => Problems.Create(http, StatusCodes.Status422UnprocessableEntity, Problems.IdempotencyKeyReused, result.Error!),
            CheckoutFailure.NotFound => Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, result.Error!),
            CheckoutFailure.NotAvailable or CheckoutFailure.ProviderUnavailable => Problems.Create(http, StatusCodes.Status502BadGateway, Problems.PaymentProviderUnavailable, result.Error!),
            _ => Problems.Create(http, StatusCodes.Status409Conflict, Problems.CheckoutFinal, result.Error ?? "This checkout has already ended."),
        };
    }

    /// <summary>A checkout this token started (anything else is "not found"), optionally acted on, as its status.</summary>
    private static async Task<IResult> CheckoutActionAsync(HttpContext http, Guid id, CheckoutService checkout, ICheckoutStore store, Func<CheckoutSession, CancellationToken, Task<CheckoutResult>>? action)
    {
        var session = await checkout.FindAsync(id, http.RequestAborted);
        if (session == null || session.CreatedBy != TokenActor(http)) return Problems.Create(http, StatusCodes.Status404NotFound, Problems.NotFound, "No such checkout.");
        if (action != null) session = (await action(session, http.RequestAborted)).Session ?? session;

        store.Reset();
        var subscription = session.TenantId is int tenant ? await store.FindSubscriptionAsync(tenant, http.RequestAborted) : null;
        var found = await store.FindPriceAsync(session.PlanPriceId, http.RequestAborted);
        return Results.Ok(new CheckoutStatusResponse(
            session.Id, session.Status.ToString(), subscription?.Status.ToString(),
            found is var (plan, price) ? new PlanSummaryResponse(plan.Id, price.Id, plan.Name, price.Cycle.ToString(), Money(price.Amount, price.Currency)) : null,
            subscription?.CurrentPeriodEndUtc is DateTime next && session.Status == CheckoutStatus.Active ? DateOnly.FromDateTime(next) : null,
            Message(session.Status)));
    }

    /// <summary>Design 7.5's texts for the website.</summary>
    private static string Message(CheckoutStatus status) => status switch
    {
        CheckoutStatus.Created or CheckoutStatus.AwaitingApproval => "Continue to PayPal to approve the subscription.",
        CheckoutStatus.Approved => "Confirming your subscription…",
        CheckoutStatus.Active => "Your subscription is active. Check your email to set your password, or go to the app.",
        CheckoutStatus.Cancelled or CheckoutStatus.Expired => "Checkout was not completed. You can try again.",
        _ => "We could not activate your subscription. Please contact us.",
    };

    public sealed record CheckoutRequest(Guid? PlanPriceId, string? Purpose, string? ReturnUrl, string? CancelUrl);

    public sealed record CheckoutCreatedResponse(Guid CheckoutSessionId, string ApprovalUrl, DateTime ExpiresAt);

    public sealed record PlanSummaryResponse(Guid PlanId, Guid PlanPriceId, string Name, string BillingCycle, MoneyResponse Price);

    public sealed record CheckoutStatusResponse(Guid CheckoutSessionId, string Status, string? SubscriptionStatus, PlanSummaryResponse? Plan, DateOnly? NextBillingDate, string Message);

    // ----------------------------------------------------------- billing access

    /// <summary>Every billing-access request takes at least this long, so known and unknown emails cannot be told apart by timing.</summary>
    public static readonly TimeSpan BillingAccessMinimumTime = TimeSpan.FromMilliseconds(400);

    public const string BillingAccessAcceptedMessage = "If this email belongs to an organization owner, we have sent a link to manage the subscription.";

    private static async Task<IResult> RequestBillingAccessAsync(HttpContext http, BillingAccessRequest? body, Inprem.Platform.Application.PublicApi.BillingAccessService access, IConfiguration configuration)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var outcome = await access.RequestAsync(body?.Email, body?.CaptchaToken, http.Connection.RemoteIpAddress?.ToString(), configuration["Links:WebsiteBaseUrl"], http.RequestAborted);
        var wait = BillingAccessMinimumTime - started.Elapsed;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, http.RequestAborted);
        return outcome switch
        {
            Inprem.Platform.Application.PublicApi.BillingAccessRequestOutcome.Invalid => Problems.Validation(http, new Dictionary<string, string[]> { ["email"] = new[] { "Give a valid email address." } }),
            Inprem.Platform.Application.PublicApi.BillingAccessRequestOutcome.Captcha => Problems.Create(http, StatusCodes.Status400BadRequest, Problems.CaptchaFailed, "The CAPTCHA check failed. Please try again."),
            _ => Results.Json(new AcceptedResponse(BillingAccessAcceptedMessage), statusCode: StatusCodes.Status202Accepted),
        };
    }

    private static async Task<IResult> CreateBillingAccessSessionAsync(HttpContext http, BillingAccessSessionRequest? body, Inprem.Platform.Application.PublicApi.BillingAccessService access)
    {
        var session = await access.ExchangeAsync(body?.Code, http.RequestAborted);
        return session is { } s
            ? Results.Json(new BillingAccessSessionResponse(s.Token, s.ExpiresUtc, s.OrganizationName), statusCode: StatusCodes.Status201Created)
            : Problems.Create(http, StatusCodes.Status400BadRequest, Problems.InvalidCode, "This link is not valid, was already used or has expired. Ask for a new one.");
    }

    private static async Task<IResult> GetAccountSubscriptionAsync(HttpContext http, BillingAccountService billing) =>
        Results.Ok(AccountSubscription(await billing.GetAsync(PublicApiClaims.TenantOf(http.User)!.Value, http.RequestAborted)));

    /// <summary>The organization's invoices, newest first (Phase 4 step 4.4); drafts are never shown.</summary>
    private static async Task<IResult> GetAccountInvoicesAsync(HttpContext http, InvoiceService invoices) =>
        Results.Ok((await invoices.ForOrganizationAsync(PublicApiClaims.TenantOf(http.User)!.Value, http.RequestAborted)).Select(i => new InvoiceResponse(
            i.Id, i.Number, i.Status, Money(i.Total, i.Currency), Money(i.AmountDue, i.Currency), i.AmountRefunded > 0 ? Money(i.AmountRefunded, i.Currency) : null,
            DateTime.SpecifyKind(i.IssueDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc), i.DueDate, i.PeriodStart, i.PeriodEnd, i.PaymentUrl)).ToList());

    private static AccountSubscriptionResponse AccountSubscription(BillingOverview o)
    {
        var s = o.Subscription;
        return new AccountSubscriptionResponse(
            s?.Status.ToString(),
            o.Plan != null && o.Price != null ? new PlanSummaryResponse(o.Plan.Id, o.Price.Id, o.Plan.Name, o.Price.Cycle.ToString(), Money(o.Price.Amount, o.Price.Currency)) : null,
            s?.Mode.ToString(), s?.TrialEndsUtc, s?.CurrentPeriodEndUtc, s?.CancelAtPeriodEnd ?? false,
            Money(s?.OutstandingBalance ?? 0m, o.Price?.Currency ?? "USD"));
    }

    private static async Task<IResult> CancelAccountSubscriptionAsync(HttpContext http, CheckoutService checkout, BillingAccountService billing)
    {
        var tenantId = PublicApiClaims.TenantOf(http.User)!.Value;
        var before = await billing.GetAsync(tenantId, http.RequestAborted);
        if (!before.PaysByPayPal) return Problems.Create(http, StatusCodes.Status409Conflict, Problems.SubscriptionNotCancellable, "There is no active PayPal subscription to cancel.");

        var result = await checkout.CancelSubscriptionAsync(tenantId, "Cancelled by the owner on the website", TokenActor(http), http.RequestAborted);
        if (!result.Succeeded)
            return result.Error?.Contains("PayPal is not answering", StringComparison.Ordinal) == true
                ? Problems.Create(http, StatusCodes.Status502BadGateway, Problems.PaymentProviderUnavailable, result.Error)
                : Problems.Create(http, StatusCodes.Status409Conflict, Problems.SubscriptionNotCancellable, result.Error ?? "The subscription cannot be cancelled.");

        var after = (await billing.GetAsync(tenantId, http.RequestAborted)).Subscription!;
        return Results.Ok(new CancellationScheduledResponse(after.Status.ToString(), true, after.CurrentPeriodEndUtc ?? DateTime.UtcNow));
    }

    public sealed record BillingAccessRequest(string? Email, string? CaptchaToken);

    public sealed record AcceptedResponse(string Message);

    public sealed record BillingAccessSessionRequest(string? Code);

    public sealed record BillingAccessSessionResponse(string BillingAccessToken, DateTime ExpiresAt, string OrganizationName);

    public sealed record AccountSubscriptionResponse(string? Status, PlanSummaryResponse? Plan, string? BillingMode, DateTime? TrialEndsAt, DateTime? CurrentPeriodEnd, bool CancelAtPeriodEnd, MoneyResponse OutstandingBalance);

    public sealed record CancellationScheduledResponse(string Status, bool CancelAtPeriodEnd, DateTime AccessEndsAt);

    public sealed record SignupOwner(string? FirstName, string? LastName, string? Email, string? Mobile);

    public sealed record SignupConsent(bool AcceptTerms, bool AcceptPrivacyPolicy, bool SmsOptIn);

    public sealed record SignupRequest(string? OrganizationName, string? Slug, string? TimeZone, SignupOwner? Owner, SignupConsent? Consent, Guid? PlanPriceId, string? Mode, string? CaptchaToken);

    public sealed record SignupCreatedResponse(Guid SignupId, string SignupToken, DateTime SignupTokenExpiresAt, string Status, string NextAction);

    public sealed record SignupStatusResponse(Guid SignupId, string Status, string? SubscriptionStatus);

    public sealed record HealthResponse(string Status);

    public sealed record MoneyResponse(string Amount, string Currency);

    public sealed record InvoiceResponse(Guid InvoiceId, string Number, string Status, MoneyResponse Total, MoneyResponse AmountDue, MoneyResponse? AmountRefunded,
        DateTime IssuedAt, DateOnly? DueDate, DateOnly? PeriodStart, DateOnly? PeriodEnd, string? PaymentUrl);

    /// <summary>
    /// Plan limits. <c>maxPeople</c> (staff and volunteers together) is what plans have today; the draft's
    /// separate staff, volunteer and admin limits wait for the plan catalogue decision (PO-5).
    /// </summary>
    public sealed record PlanLimits(int? MaxPeople);

    public sealed record PlanPriceResponse(Guid PlanPriceId, string BillingCycle, MoneyResponse Price);

    public sealed record PlanResponse(Guid PlanId, string Name, string? Description, int TrialDays, IReadOnlyList<string> Features, PlanLimits Limits, IReadOnlyList<PlanPriceResponse> Prices);

    public sealed record TimeZoneResponse(string Id, string DisplayName);

    public sealed record SlugAvailabilityResponse(string Slug, bool Available, string? Suggestion);
}
