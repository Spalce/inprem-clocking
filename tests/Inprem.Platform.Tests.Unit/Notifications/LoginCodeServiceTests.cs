using Inprem.Platform.Application.Notifications;
using Xunit;
using static Inprem.Platform.Tests.Unit.Notifications.NotificationServiceTests;

namespace Inprem.Platform.Tests.Unit.Notifications;

/// <summary>Phase 5 step 5.2: the limits around login codes, on top of Verify's own, and what the audit log keeps.</summary>
public class LoginCodeServiceTests
{
    private const string Phone = "+12025550123";

    private readonly MovableClock _clock = new(new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc));
    private readonly MemoryStore _store = new();
    private readonly FakeOtp _otp = new();

    private LoginCodeService Service => new(_otp, _store, _clock);

    private sealed class FakeOtp : IOtpService
    {
        public List<string> Sent { get; } = new();
        public Queue<OtpCheck> Checks { get; } = new();
        public bool Down { get; set; }
        public bool IsStandIn => true;

        public Task<OtpSendResult> SendAsync(string phoneE164, CancellationToken ct = default)
        {
            if (Down) return Task.FromResult(new OtpSendResult(false, "unreachable", Temporary: true));
            Sent.Add(phoneE164);
            return Task.FromResult(OtpSendResult.Ok);
        }

        public Task<OtpCheck> CheckAsync(string phoneE164, string code, CancellationToken ct = default) =>
            Task.FromResult(Checks.TryDequeue(out var result) ? result : OtpCheck.Approved);
    }

    [Fact]
    public async Task A_Code_Goes_To_The_Number_In_E164_And_The_Log_Keeps_It_Masked()
    {
        var sent = await Service.SendAsync("u1", "+1 (202) 555-0123", LoginCodePurposes.ProviderSignIn);

        Assert.True(sent.Sent);
        Assert.Contains("+1202•••0123", sent.Message);
        Assert.Equal(Phone, Assert.Single(_otp.Sent));
        var audit = Assert.Single(_store.Audit);
        Assert.Equal(("security.sms-code-sent", "user:u1", "+1202•••0123"), (audit.Action, audit.Actor, audit.Target));
    }

    [Fact]
    public async Task A_Number_Without_Its_Country_Code_Gets_No_Code()
    {
        var sent = await Service.SendAsync("u1", "202 555 0123", LoginCodePurposes.ConfirmPhone);
        Assert.Equal((false, LoginCodeRefusal.InvalidPhone), (sent.Sent, sent.Refusal));
        Assert.Empty(_otp.Sent);
    }

    [Fact]
    public async Task Three_Codes_In_Ten_Minutes_Then_Wait()
    {
        for (var i = 0; i < 3; i++) Assert.True((await Service.SendAsync("u1", Phone, LoginCodePurposes.AppSignIn)).Sent);
        Assert.Equal(LoginCodeRefusal.TooMany, (await Service.SendAsync("u1", Phone, LoginCodePurposes.AppSignIn)).Refusal);
        Assert.True((await Service.SendAsync("u2", Phone, LoginCodePurposes.AppSignIn)).Sent); // per account

        _clock.Set(_clock.UtcNow.AddMinutes(11));
        Assert.True((await Service.SendAsync("u1", Phone, LoginCodePurposes.AppSignIn)).Sent);
    }

    [Fact]
    public async Task Ten_Codes_A_Day_At_Most()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.True((await Service.SendAsync("u1", Phone, LoginCodePurposes.AppSignIn)).Sent);
            _clock.Set(_clock.UtcNow.AddMinutes(11));
        }
        Assert.Equal(LoginCodeRefusal.TooMany, (await Service.SendAsync("u1", Phone, LoginCodePurposes.AppSignIn)).Refusal);
    }

    [Fact]
    public async Task A_Send_That_Failed_Does_Not_Count_Against_The_Limit()
    {
        _otp.Down = true;
        for (var i = 0; i < 4; i++) Assert.Equal(LoginCodeRefusal.Unavailable, (await Service.SendAsync("u1", Phone, LoginCodePurposes.AppSignIn)).Refusal);
        _otp.Down = false;
        Assert.True((await Service.SendAsync("u1", Phone, LoginCodePurposes.AppSignIn)).Sent);
    }

    [Fact]
    public async Task Five_Wrong_Codes_Stop_Further_Tries_For_Fifteen_Minutes()
    {
        for (var i = 0; i < 5; i++)
        {
            _otp.Checks.Enqueue(OtpCheck.Wrong);
            Assert.Equal(OtpCheck.Wrong, await Service.CheckAsync("u1", Phone, "111111", LoginCodePurposes.AppSignIn));
        }
        Assert.Equal(OtpCheck.TooManyAttempts, await Service.CheckAsync("u1", Phone, "123456", LoginCodePurposes.AppSignIn)); // even the right code

        _clock.Set(_clock.UtcNow.AddMinutes(16));
        Assert.Equal(OtpCheck.Approved, await Service.CheckAsync("u1", Phone, "123456", LoginCodePurposes.AppSignIn));
        Assert.Equal("security.sms-code-approved", _store.Audit[^1].Action);
        Assert.DoesNotContain(_store.Audit, a => (a.Details ?? "").Contains("123456")); // never the code
    }

    [Fact]
    public async Task A_Code_That_Is_Not_Digits_Counts_As_Wrong_Without_Asking_Verify()
    {
        _otp.Checks.Enqueue(OtpCheck.Approved);
        Assert.Equal(OtpCheck.Wrong, await Service.CheckAsync("u1", Phone, "abc", LoginCodePurposes.AppSignIn));
        Assert.Single(_otp.Checks); // not asked
        Assert.Equal(OtpCheck.Approved, await Service.CheckAsync("u1", Phone, " 123 456 ", LoginCodePurposes.AppSignIn)); // spaces are fine
    }

    [Fact]
    public async Task Verify_Out_Of_Reach_Is_Not_A_Wrong_Code()
    {
        for (var i = 0; i < 6; i++)
        {
            _otp.Checks.Enqueue(OtpCheck.Unavailable);
            Assert.Equal(OtpCheck.Unavailable, await Service.CheckAsync("u1", Phone, "123456", LoginCodePurposes.AppSignIn));
        }
        Assert.Equal(OtpCheck.Approved, await Service.CheckAsync("u1", Phone, "123456", LoginCodePurposes.AppSignIn));
    }
}
