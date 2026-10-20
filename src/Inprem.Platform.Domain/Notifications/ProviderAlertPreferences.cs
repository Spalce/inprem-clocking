namespace Inprem.Platform.Domain.Notifications;

public enum AlertDelivery
{
    /// <summary>A message each time.</summary>
    Immediate,

    /// <summary>One message a day with everything of the last day.</summary>
    DailySummary,
    Off,
}

/// <summary>
/// Which alerts a console user wants (Phase 5 step 5.3, design Appendix C: "configurable per provider
/// user", "immediate or daily summary"). A user without a row gets the defaults: new signups and
/// billing problems at once, system problems on. Who may receive each kind is decided by role
/// (billing problems: Owner and Billing; system problems: Owners), not here.
/// </summary>
public sealed class ProviderAlertPreferences
{
    private ProviderAlertPreferences()
    {
        UserId = "";
    }

    /// <summary>The console user's account (AspNetUsers.Id).</summary>
    public string UserId { get; private set; }
    public bool NewSignups { get; private set; }
    public AlertDelivery BillingProblems { get; private set; }
    public bool SystemProblems { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static ProviderAlertPreferences Defaults(string userId) =>
        new() { UserId = userId, NewSignups = true, BillingProblems = AlertDelivery.Immediate, SystemProblems = true };

    public void Change(bool newSignups, AlertDelivery billingProblems, bool systemProblems, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (!Enum.IsDefined(billingProblems)) throw new ArgumentOutOfRangeException(nameof(billingProblems));
        NewSignups = newSignups;
        BillingProblems = billingProblems;
        SystemProblems = systemProblems;
        UpdatedUtc = nowUtc;
    }
}
