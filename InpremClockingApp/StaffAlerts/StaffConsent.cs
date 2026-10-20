using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace InpremClockingApp.StaffAlerts;

public enum PersonKind
{
    Staff,
    Volunteer,
}

/// <summary>
/// Records a staff member's or volunteer's agreement to texts (Phase 6 step 6.2, C3-8), only when the
/// organization has staff alerts on. The number is the one saved on the person (the approved forms
/// already ask for it), so what was agreed to and what is texted are the same number. A number without
/// its country code cannot be texted and is not recorded: the person is told to add it.
/// </summary>
public sealed class StaffConsentRecorder
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly StaffAlertSettings _settings;
    private readonly NotificationService _notifications;

    public StaffConsentRecorder(ApplicationDbContext db, ITenantContext tenant, StaffAlertSettings settings, NotificationService notifications)
    {
        _db = db;
        _tenant = tenant;
        _settings = settings;
        _notifications = notifications;
    }

    public static string PersonRef(PersonKind kind, long id) => $"{(kind == PersonKind.Staff ? "staff" : "volunteer")}:{id}";

    /// <summary>The person's id in this organization by email (the back-office editor knows no id for a new person).</summary>
    public async Task<long?> FindByEmailAsync(PersonKind kind, string? email, CancellationToken ct = default)
    {
        var address = email?.Trim();
        if (string.IsNullOrEmpty(address)) return null;
        return kind == PersonKind.Staff
            ? await _db.Staffs.AsNoTracking().Where(s => s.EmailAddress == address).Select(s => (long?)s.StaffId).FirstOrDefaultAsync(ct)
            : await _db.Volunteers.AsNoTracking().Where(v => v.EmailAddress == address).Select(v => (long?)v.VolunteerId).FirstOrDefaultAsync(ct);
    }

    /// <summary>Null when recorded; otherwise why not (staff alerts off, person not found, number without country code).</summary>
    public async Task<string?> RecordAsync(PersonKind kind, long personId, string source, string actor, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not int tenantId) return "No organization.";
        if (!(await _settings.GetAsync(tenantId, ct)).Enabled) return "Staff alerts are off for this organization.";
        var phone = kind == PersonKind.Staff
            ? await _db.Staffs.AsNoTracking().Where(s => s.StaffId == personId).Select(s => s.PhoneNumber).FirstOrDefaultAsync(ct)
            : await _db.Volunteers.AsNoTracking().Where(v => v.VolunteerId == personId).Select(v => v.PhoneNumber).FirstOrDefaultAsync(ct);
        if (phone == null) return "The person was not found.";
        return await _notifications.RecordConsentAsync(phone, tenantId, PersonRef(kind, personId), source, ConsentWording.StaffAlerts, actor, ct)
            ? null
            : "To get texts, the phone number needs its country code, e.g. +1 614 516 1812. Edit the number, then tick the box again.";
    }
}

/// <summary>
/// The kiosk registration forms (StaffAttendance, VolunteerAttendance; approved pages, not edited beyond
/// the one line that includes the consent box): after a registration that succeeded (the page sends
/// the person to their clock page), the ticked box is recorded. Any other request passes untouched.
/// </summary>
public sealed class KioskConsentPageFilter : IAsyncPageFilter
{
    public const string FieldName = "SmsConsent";

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method) || !request.HasFormContentType || request.Form[FieldName] != "true") return;
        if (executed.Result is not RedirectResult redirect) return;

        var (kind, prefix) = redirect.Url.StartsWith("/staff-clockin/", StringComparison.OrdinalIgnoreCase) ? (PersonKind.Staff, "/staff-clockin/")
            : redirect.Url.StartsWith("/volunteer-clockin/", StringComparison.OrdinalIgnoreCase) ? (PersonKind.Volunteer, "/volunteer-clockin/")
            : ((PersonKind?)null, "");
        if (kind == null || !long.TryParse(redirect.Url[prefix.Length..], out var personId)) return;

        try
        {
            var recorder = context.HttpContext.RequestServices.GetRequiredService<StaffConsentRecorder>();
            await recorder.RecordAsync(kind.Value, personId, ConsentSources.KioskRegistration,
                $"kiosk:user:{context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)}", context.HttpContext.RequestAborted);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The registration itself succeeded; without a consent record the person simply gets no texts.
        }
    }
}
