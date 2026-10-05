using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class VolunteerClockingService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantClock _tenantClock;

    public VolunteerClockingService(ApplicationDbContext db, ITenantClock tenantClock)
    {
        _db = db;
        _tenantClock = tenantClock;
    }

    // New helper to return volunteer clocking report rows.
    // start/end are org-local wall-clock boundaries (e.g. from a date picker); converted to UTC for the query.
    public async Task<List<VolunteerClockingVm>> GetClockingReport(DateTime start, DateTime end)
    {
        var startUtc = _tenantClock.ToUtc(start);
        var endUtc = _tenantClock.ToUtc(end);

        var record = await _db.Clockings
            .Where(e => (e.ClockInTime >= startUtc && e.ClockInTime <= endUtc) || (e.ClockOutTime != null && e.ClockOutTime >= startUtc && e.ClockOutTime <= endUtc))
            .ToListAsync().ConfigureAwait(false);

        var list = record.Select(e => new VolunteerClockingVm
        {
            Clocking = new List<Clocking> { e }
        }).ToList();

        return list;
    }

    public async Task<List<VolunteerClockingVm>> GetClockingReportForVolunteer(int volunteerId, DateTime start, DateTime end)
    {
        var startUtc = _tenantClock.ToUtc(start);
        var endUtc = _tenantClock.ToUtc(end);

        var record = await _db.Clockings
            .Where(e => e.VoluntId == volunteerId && ((e.ClockInTime >= startUtc && e.ClockInTime <= endUtc) || (e.ClockOutTime != null && e.ClockOutTime >= startUtc && e.ClockOutTime <= endUtc)))
            .ToListAsync().ConfigureAwait(false);

        var list = record.Select(e => new VolunteerClockingVm
        {
            Clocking = new List<Clocking> { e }
        }).ToList();

        return list;
    }

    public async Task<IEnumerable<Clocking>> GetAll()
    {
        return await _db.Clockings.ToListAsync().ConfigureAwait(false);
    }

    // Paginated variant of GetClockingReport/GetClockingReportForVolunteer, for report pages
    // backed by potentially large date ranges. Preserves the same clock-in-or-clock-out range filter.
    public async Task<PagedResult<VolunteerClockingVm>> GetClockingReportPaged(DateTime start, DateTime end, int? volunteerId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var startUtc = _tenantClock.ToUtc(start);
        var endUtc = _tenantClock.ToUtc(end);

        var query = _db.Clockings
            .Where(e => (e.ClockInTime >= startUtc && e.ClockInTime <= endUtc) || (e.ClockOutTime != null && e.ClockOutTime >= startUtc && e.ClockOutTime <= endUtc));

        if (volunteerId.HasValue)
            query = query.Where(e => e.VoluntId == volunteerId.Value);

        query = query.OrderByDescending(e => e.CreatedAt!.Value);

        var total = await query.CountAsync().ConfigureAwait(false);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync().ConfigureAwait(false);

        var list = items.Select(e => new VolunteerClockingVm
        {
            Clocking = new List<Clocking> { e }
        }).ToList();

        return new PagedResult<VolunteerClockingVm>
        {
            Items = list,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    // start/end, when provided, are org-local wall-clock boundaries; converted to UTC for the query.
    public async Task<PagedResult<Clocking>> GetPaged(int page, int pageSize, int? volunteerId = null, DateTime? start = null, DateTime? end = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _db.Clockings.AsQueryable();

        if (volunteerId.HasValue)
            query = query.Where(e => e.VoluntId == volunteerId.Value);

        if (start.HasValue)
        {
            var startUtc = _tenantClock.ToUtc(start.Value);
            query = query.Where(e => e.ClockInTime >= startUtc);
        }

        if (end.HasValue)
        {
            var endUtc = _tenantClock.ToUtc(end.Value);
            query = query.Where(e => e.ClockInTime <= endUtc);
        }

        query = query.OrderByDescending(e => e.CreatedAt!.Value);

        var total = await query.CountAsync().ConfigureAwait(false);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync().ConfigureAwait(false);

        return new PagedResult<Clocking>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IEnumerable<Clocking>> GetAllToday()
    {
        var today = _tenantClock.TodayLocalDate();
        return await _db.Clockings.Where(e => e.ClockDate == today).ToListAsync().ConfigureAwait(false);
    }

    // Single canonical lookup for "this volunteer's session for today, if any" - used both to
    // decide whether a new clock-in is allowed and to find the record clock-out/break actions
    // should mutate, so there's exactly one place that defines what "today's session" means.
    public async Task<Clocking?> GetTodayRecord(long volunteerId)
    {
        var today = _tenantClock.TodayLocalDate();
        return await _db.Clockings
            .FirstOrDefaultAsync(e => e.VoluntId == volunteerId && e.ClockDate == today)
            .ConfigureAwait(false);
    }

    public async Task<bool> ClockOut(Clocking model)
    {
        var item = await _db.Clockings.FindAsync(model.ClockingId).ConfigureAwait(false);
        if (item == null!)
        {
            return false;
        }

        if (item.ClockOutTime == null)
        {
            var now = DateTime.UtcNow;

            if (item.LeaveOnBreakTime != null &&
                item.ReturnOnBreakTime == null)
            {
                item.ReturnOnBreakTime = now;
            }

            item.ClockOutTime = now;
            TimeSpan? main = now - item.ClockInTime;
            TimeSpan? difference;
            if (item is { LeaveOnBreakTime: { }, ReturnOnBreakTime: { } })
            {
                var leave = item.ReturnOnBreakTime - item.LeaveOnBreakTime;
                difference = main - leave;
            }
            else
            {
                difference = main;
            }

            // WorkingHours is stored as a SQL `time` column (00:00:00 to 23:59:59.9999999) -
            // clamp into that range so a bad/stale ClockInTime (negative span) or a session left
            // open for more than a day (span >= 24h) can't throw an unhandled SqlDbType.Time
            // overflow and 500 the request. Either case reflects bad underlying data, not a
            // value we can compute correctly, so clamping to the nearest valid bound is the
            // safest fallback short of rejecting the clock-out outright.
            if (difference.HasValue)
            {
                if (difference.Value < TimeSpan.Zero)
                    difference = TimeSpan.Zero;
                else if (difference.Value >= TimeSpan.FromDays(1))
                    difference = TimeSpan.FromDays(1) - TimeSpan.FromTicks(1);
            }

            item.WorkingHours = difference;
        }
        else
        {
            return false;
        }

        _db.Clockings.Update(item);
        await _db.SaveChangesAsync();

        return true;
    }

    // Admin correction of an existing, already-completed record (the "Edit" action on
    // VolunteerClocking) - the one gap flagged most prominently in backoffice-review.md. Only the
    // clocking-window fields are touched; the owning volunteer and the record's calendar day are
    // left alone, since reassigning those is a different, riskier operation than fixing a
    // mistyped timestamp. All timestamps are expected already converted to UTC by the caller.
    public async Task<(bool Success, string? Error)> UpdateTimes(
        long id, DateTime? clockIn, DateTime? clockOut, DateTime? leaveOnBreak, DateTime? returnOnBreak)
    {
        var item = await _db.Clockings.FindAsync(id).ConfigureAwait(false);
        if (item == null)
            return (false, "Clocking record not found.");

        if (clockIn == null)
            return (false, "Clock In is required.");

        if (clockOut.HasValue && clockOut <= clockIn)
            return (false, "Clock Out must be after Clock In.");

        if (leaveOnBreak.HasValue != returnOnBreak.HasValue)
            return (false, "Enter both Break Start and Break End, or leave both blank.");

        if (leaveOnBreak.HasValue && returnOnBreak.HasValue && returnOnBreak <= leaveOnBreak)
            return (false, "Break End must be after Break Start.");

        TimeSpan? workingHours = null;
        if (clockOut.HasValue)
        {
            workingHours = clockOut - clockIn;
            if (leaveOnBreak.HasValue && returnOnBreak.HasValue)
                workingHours -= returnOnBreak - leaveOnBreak;

            // WorkingHours is stored as a SQL `time` column (00:00:00 to 23:59:59.9999999) - same
            // bound already enforced by the live Clock Out action (see ClockOut above).
            if (workingHours < TimeSpan.Zero || workingHours >= TimeSpan.FromDays(1))
                return (false, "The resulting working hours must be between 0 and 24 hours - check the break times.");
        }

        item.ClockInTime = clockIn;
        item.ClockOutTime = clockOut;
        item.LeaveOnBreakTime = leaveOnBreak;
        item.ReturnOnBreakTime = returnOnBreak;
        item.WorkingHours = workingHours;

        _db.Clockings.Update(item);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        return (true, null);
    }

    public async Task<bool> BreakStart(Clocking model)
    {
        var item = await _db.Clockings.FindAsync(model.ClockingId).ConfigureAwait(false);
        if (item == null!)
        {
            return false;
        }

        if (item.ClockOutTime == null)
        {
            if (item.LeaveOnBreakTime == null)
            {
                item!.LeaveOnBreakTime = DateTime.UtcNow;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        _db.Clockings.Update(item);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> BreakEnd(Clocking model)
    {
        var item = await _db.Clockings.FindAsync(model.ClockingId).ConfigureAwait(false);
        if (item == null!)
        {
            return false;
        }

        if (item.ClockOutTime == null)
        {
            if (item.ReturnOnBreakTime == null)
            {
                item!.ReturnOnBreakTime = DateTime.UtcNow;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        _db.Clockings.Update(item);
        await _db.SaveChangesAsync();

        return true;
    }
    public async Task<bool> CheckToday(Clocking model)
    {
        var today = _tenantClock.TodayLocalDate();
        return await _db.Clockings
            .AnyAsync(e => e.VoluntId == model.VoluntId && e.ClockDate == today)
            .ConfigureAwait(false);
    }

    public async Task<Clocking> Create(Clocking model)
    {
        try
        {
            model.ClockDate = _tenantClock.TodayLocalDate();

            var exists = await _db.Clockings
                .AnyAsync(e => e.VoluntId == model.VoluntId && e.ClockDate == model.ClockDate)
                .ConfigureAwait(false);
            if (exists)
            {
                return null!;
            }

            await _db.Clockings.AddAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (DbUpdateException ex) when (IsDuplicateClockingViolation(ex))
        {
            // Lost a race with a concurrent clock-in for the same volunteer/day. The unique
            // index on (VoluntId, ClockDate) is the real guarantee here; the check above is just
            // the fast path that avoids hitting the constraint in the common, non-racing case.
            return null!;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private static bool IsDuplicateClockingViolation(DbUpdateException ex)
    {
        // SQL Server: 2601 = duplicate key on a unique index, 2627 = unique constraint violation.
        return ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }

    public async Task<bool> CreateBatch(List<Clocking> model)
    {
        try
        {
            await _db.Clockings.AddRangeAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
