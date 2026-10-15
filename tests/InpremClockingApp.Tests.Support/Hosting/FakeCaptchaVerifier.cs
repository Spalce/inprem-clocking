using Inprem.Platform.Application.PublicApi;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>The test host's CAPTCHA: the answer "pass" passes, anything else fails.</summary>
public sealed class FakeCaptchaVerifier : ICaptchaVerifier
{
    public const string Pass = "pass";

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default) => Task.FromResult(token == Pass);
}
