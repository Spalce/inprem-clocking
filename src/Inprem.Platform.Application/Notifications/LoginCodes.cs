using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Notifications;

namespace Inprem.Platform.Application.Notifications;

public enum OtpCheck
{
    Approved,

    /// <summary>Not the code; the person may try again.</summary>
    Wrong,

    /// <summary>No code waiting: never sent, expired (10 minutes) or already used.</summary>
    ExpiredOrUsed,

    /// <summary>Too many wrong codes: ask for a new one later.</summary>
    TooManyAttempts,

    /// <summary>The code service could not be reached.</summary>
    Unavailable,
}

public sealed record OtpSendResult(bool Sent, string? Error = null, bool Temporary = false)
{
    public static readonly OtpSendResult Ok = new(true);
}

/// <summary>
/// Login codes by text (Phase 5 step 5.2, decision D14): Twilio Verify, or the stand-in until it is set
/// up. Verify makes and keeps the code; a code works once, for 10 minutes, for a few tries.
/// </summary>
public interface IOtpService
{
    bool IsStandIn { get; }
    Task<OtpSendResult> SendAsync(string phoneE164, CancellationToken ct = default);
    Task<OtpCheck> CheckAsync(string phoneE164, string code, CancellationToken ct = default);
}

public enum LoginCodeRefusal
{
    None,
    InvalidPhone,

    /// <summary>Too many codes asked for (3 in 10 minutes, 10 a day per account).</summary>
    TooMany,
    Unavailable,
}

public sealed record LoginCodeSent(bool Sent, LoginCodeRefusal Refusal, string Message)
{
    public static LoginCodeSent Ok(string maskedPhone) => new(true, LoginCodeRefusal.None, $"We sent a code to {maskedPhone}. It works for 10 minutes.");
}

/// <summary>Where a code is used: recorded with each attempt and in the audit log.</summary>
public static class LoginCodePurposes
{
    public const string ProviderSignIn = "provider-sign-in";
    public const string AppSignIn = "app-sign-in";
    public const string ConfirmPhone = "confirm-phone";
}

/// <summary>
/// The rules around login codes (Phase 5 step 5.2): how often an account may ask for a code and try
/// one, on top of Verify's own limits; every send and every result in the audit log, with the number
/// masked and never the code. The caller decides what an approved code unlocks.
/// </summary>
public sealed class LoginCodeService
{
    public const int MaxSendsPer10Minutes = 3;
    public const int MaxSendsPerDay = 10;
    public const int MaxWrongPer15Minutes = 5;

    public const string SendKind = "sms-code-send";
    public const string CheckKind = "sms-code-check";

    private readonly IOtpService _otp;
    private readonly INotificationStore _store;
    private readonly IClock _clock;

    public LoginCodeService(IOtpService otp, INotificationStore store, IClock clock)
    {
        _otp = otp;
        _store = store;
        _clock = clock;
    }

    public bool IsStandIn => _otp.IsStandIn;

    public async Task<LoginCodeSent> SendAsync(string accountId, string? phone, string purpose, CancellationToken ct = default)
    {
        var e164 = PhoneNumber.ToE164(phone);
        if (e164 == null) return new LoginCodeSent(false, LoginCodeRefusal.InvalidPhone, "Enter the mobile number with its country code, e.g. +1 202 555 0123.");
        var now = _clock.UtcNow;
        if (await _store.CountAttemptsAsync(SendKind, accountId, now.AddMinutes(-10), succeededOnly: true, ct: ct).ConfigureAwait(false) >= MaxSendsPer10Minutes
            || await _store.CountAttemptsAsync(SendKind, accountId, now.AddDays(-1), succeededOnly: true, ct: ct).ConfigureAwait(false) >= MaxSendsPerDay)
        {
            await AuditAsync(accountId, "security.sms-code-refused", e164, $"Too many codes asked for ({purpose}).", now, ct).ConfigureAwait(false);
            return new LoginCodeSent(false, LoginCodeRefusal.TooMany, "Too many codes were asked for. Wait 10 minutes, or use another way to sign in.");
        }

        OtpSendResult result;
        try
        {
            result = await _otp.SendAsync(e164, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            result = new OtpSendResult(false, e.GetType().Name, Temporary: true);
        }
        await _store.AddAttemptAsync(SendKind, accountId, result.Sent, now, ct).ConfigureAwait(false);
        await AuditAsync(accountId, result.Sent ? "security.sms-code-sent" : "security.sms-code-failed", e164,
            result.Sent ? $"Code sent ({purpose})." : $"The code could not be sent ({purpose}): {result.Error}.", now, ct).ConfigureAwait(false);
        return result.Sent
            ? LoginCodeSent.Ok(PhoneNumber.Mask(e164))
            : new LoginCodeSent(false, LoginCodeRefusal.Unavailable, "The code could not be sent just now. Try again in a minute, or use another way to sign in.");
    }

    public async Task<OtpCheck> CheckAsync(string accountId, string? phone, string? code, string purpose, CancellationToken ct = default)
    {
        var e164 = PhoneNumber.ToE164(phone);
        var digits = new string((code ?? "").Where(char.IsAsciiDigit).ToArray());
        var now = _clock.UtcNow;
        if (await _store.CountAttemptsAsync(CheckKind, accountId, now.AddMinutes(-15), failedOnly: true, ct: ct).ConfigureAwait(false) >= MaxWrongPer15Minutes)
            return OtpCheck.TooManyAttempts;
        if (e164 == null || digits.Length is < 4 or > 10)
        {
            await _store.AddAttemptAsync(CheckKind, accountId, false, now, ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return OtpCheck.Wrong;
        }

        OtpCheck result;
        try
        {
            result = await _otp.CheckAsync(e164, digits, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            result = OtpCheck.Unavailable;
        }
        if (result != OtpCheck.Unavailable) await _store.AddAttemptAsync(CheckKind, accountId, result == OtpCheck.Approved, now, ct).ConfigureAwait(false);
        await AuditAsync(accountId, result == OtpCheck.Approved ? "security.sms-code-approved" : "security.sms-code-rejected", e164, $"{result} ({purpose}).", now, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>A phone for codes confirmed with a code, or removed (the number masked in the audit log).</summary>
    public Task RecordPhoneChangeAsync(string accountId, string? phone, bool confirmed, CancellationToken ct = default) =>
        AuditAsync(accountId, confirmed ? "security.phone-confirmed" : "security.phone-removed", PhoneNumber.ToE164(phone) ?? "",
            confirmed ? "Phone for sign-in codes confirmed with a code." : "Phone for sign-in codes removed.", _clock.UtcNow, ct);

    private async Task AuditAsync(string accountId, string action, string e164, string details, DateTime now, CancellationToken ct)
    {
        await _store.AddAuditAsync(AuditEntry.Record($"user:{accountId}", action, PhoneNumber.Mask(e164), details, now), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
