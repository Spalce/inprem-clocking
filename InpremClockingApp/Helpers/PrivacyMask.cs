namespace InpremClockingApp.Helpers;

// Shared by the Staff/Volunteer Attendance kiosk pages' same-name-match picker: when multiple
// registered people share an identical first+last name, we list them so the walk-up user can pick
// which record is theirs, but must not show a stranger someone else's full email address.
public static class PrivacyMask
{
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "—";

        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";

        var local = email[..at];
        var domain = email[at..];

        var prefixLen = local.Length <= 2 ? 1 : 2;
        var suffixLen = local.Length <= 2 ? 0 : 2;
        if (prefixLen + suffixLen >= local.Length)
        {
            // Keep at least one character masked in the middle - a short local part (3-4 chars)
            // showing a 2-char prefix and suffix would otherwise reveal the whole address.
            suffixLen = Math.Max(0, local.Length - prefixLen - 1);
        }

        var prefix = local[..prefixLen];
        var suffix = suffixLen > 0 ? local[^suffixLen..] : "";
        return prefix + "***" + suffix + domain;
    }
}
