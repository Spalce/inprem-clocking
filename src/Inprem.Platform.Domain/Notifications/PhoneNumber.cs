namespace Inprem.Platform.Domain.Notifications;

/// <summary>
/// Phone numbers for texts are stored in E.164 (design Appendix C): "+", the country code and the
/// number, 8 to 15 digits in all, e.g. +12025550123. Spaces, dashes, dots and brackets are removed;
/// a leading "00" counts as "+". A number without its country code cannot be texted safely (which
/// country?), so it is refused rather than guessed.
/// </summary>
public static class PhoneNumber
{
    /// <summary>The number in E.164, or null when it cannot be one.</summary>
    public static string? ToE164(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();
        foreach (var c in new[] { " ", "-", ".", "(", ")", " " }) text = text.Replace(c, "");
        if (text.StartsWith("00", StringComparison.Ordinal)) text = "+" + text[2..];
        if (text.Length < 2 || text[0] != '+' || text[1] == '0') return null;
        var digits = text[1..];
        if (digits.Length is < 8 or > 15 || !digits.All(char.IsAsciiDigit)) return null;
        return text;
    }

    /// <summary>For logs and screens that must not show the whole number: +1202•••0123.</summary>
    public static string Mask(string? e164) =>
        string.IsNullOrEmpty(e164) || e164.Length < 9 ? "•••" : e164[..5] + "•••" + e164[^4..];
}
