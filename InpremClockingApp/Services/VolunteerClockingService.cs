using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class VolunteerClockingService
{
    private readonly ApplicationDbContext _db;

    public VolunteerClockingService(ApplicationDbContext db)
    {
        _db = db;
    }

    // New helper to return volunteer clocking report rows.
    // start/end are org-local wall-clock boundaries (e.g. from a date picker); converted to UTC for the query.
    public async Task<List<VolunteerClockingVm>> GetClockingReport(DateTime start, DateTime end)
    {
        var startUtc = OrgClock.ToUtc(start);
        var endUtc = OrgClock.ToUtc(end);

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
        var startUtc = OrgClock.ToUtc(start);
        var endUtc = OrgClock.ToUtc(end);

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
            var startUtc = OrgClock.ToUtc(start.Value);
            query = query.Where(e => e.ClockInTime >= startUtc);
        }

        if (end.HasValue)
        {
            var endUtc = OrgClock.ToUtc(end.Value);
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
        var today = OrgClock.TodayLocalDate();
        return await _db.Clockings.Where(e => e.ClockDate == today).ToListAsync().ConfigureAwait(false);
    }

    // Single canonical lookup for "this volunteer's session for today, if any" - used both to
    // decide whether a new clock-in is allowed and to find the record clock-out/break actions
    // should mutate, so there's exactly one place that defines what "today's session" means.
    public async Task<Clocking?> GetTodayRecord(long volunteerId)
    {
        var today = OrgClock.TodayLocalDate();
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
        var today = OrgClock.TodayLocalDate();
        return await _db.Clockings
            .AnyAsync(e => e.VoluntId == model.VoluntId && e.ClockDate == today)
            .ConfigureAwait(false);
    }

    public async Task<Clocking> Create(Clocking model)
    {
        try
        {
            model.ClockDate = OrgClock.TodayLocalDate();

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
