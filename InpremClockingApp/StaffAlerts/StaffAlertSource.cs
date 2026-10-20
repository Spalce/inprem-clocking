using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Data;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.StaffAlerts;

/// <summary>
/// What the staff alerts check reads (Phase 6 step 6.3): the active organizations that switched staff
/// alerts on (platform settings, the organization's time zone), and their sessions not clocked out.
/// A background job has no signed-in organization, so the approved app's query filters are bypassed
/// on purpose and every query names the organization itself. Read-only.
/// </summary>
public sealed class StaffAlertSource : IStaffAlertSource
{
    private readonly ApplicationDbContext _app;
    private readonly PlatformDbContext _platform;

    public StaffAlertSource(ApplicationDbContext app, PlatformDbContext platform)
    {
        _app = app;
        _platform = platform;
    }

    public async Task<IReadOnlyList<StaffAlertOrganization>> OrganizationsWithAlertsAsync(CancellationToken ct = default)
    {
        var settings = await _platform.TenantMessagingSettings.AsNoTracking().Where(s => s.StaffAlertsEnabled).ToListAsync(ct);
        if (settings.Count == 0) return Array.Empty<StaffAlertOrganization>();
        var ids = settings.Select(s => s.TenantId).ToList();
        var zones = await _app.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => ids.Contains(t.Id) && t.IsActive)
            .ToDictionaryAsync(t => t.Id, t => t.TimeZoneId, ct);
        return settings.Where(s => zones.ContainsKey(s.TenantId))
            .Select(s => new StaffAlertOrganization(s.TenantId, zones[s.TenantId],
                new StaffAlertChoices(true, s.MissedClockOutTime, s.LongBreakMinutes, s.AdminSummaryEnabled)))
            .ToList();
    }

    public async Task<IReadOnlyList<OpenClocking>> OpenClockingsAsync(int tenantId, DateOnly clockDate, CancellationToken ct = default)
    {
        var day = clockDate.ToDateTime(TimeOnly.MinValue);
        var staff = await _app.ClockingsStaff.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ClockDate == day && c.ClockInTime != null && c.ClockOutTime == null && c.Staff!.TenantId == tenantId)
            .Select(c => new { c.StafId, c.Staff!.FirstName, c.Staff.LastName, c.Staff.PhoneNumber, ClockIn = c.ClockInTime!.Value, c.LeaveOnBreakTime, c.ReturnOnBreakTime })
            .ToListAsync(ct);
        var volunteers = await _app.Clockings.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ClockDate == day && c.ClockInTime != null && c.ClockOutTime == null && c.Volunteer!.TenantId == tenantId)
            .Select(c => new { c.VoluntId, c.Volunteer!.FirstName, c.Volunteer.LastName, c.Volunteer.PhoneNumber, ClockIn = c.ClockInTime!.Value, c.LeaveOnBreakTime, c.ReturnOnBreakTime })
            .ToListAsync(ct);

        return staff.Select(c => new OpenClocking(StaffConsentRecorder.PersonRef(PersonKind.Staff, c.StafId), c.FirstName, $"{c.FirstName} {c.LastName}".Trim(),
                c.PhoneNumber, Utc(c.ClockIn), c.ReturnOnBreakTime == null ? Utc(c.LeaveOnBreakTime) : null))
            .Concat(volunteers.Select(c => new OpenClocking(StaffConsentRecorder.PersonRef(PersonKind.Volunteer, c.VoluntId), c.FirstName, $"{c.FirstName} {c.LastName}".Trim(),
                c.PhoneNumber, Utc(c.ClockIn), c.ReturnOnBreakTime == null ? Utc(c.LeaveOnBreakTime) : null)))
            .ToList();
    }

    // The app stores UTC instants without a kind.
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;
}
