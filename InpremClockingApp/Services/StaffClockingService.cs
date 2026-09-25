using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class StaffClockingService
{
    private readonly ApplicationDbContext _db;

    public StaffClockingService(ApplicationDbContext db)
    {
        _db = db;
    }

    // New helper to return report rows for staff clocking.
    // start/end are org-local wall-clock boundaries (e.g. from a date picker); converted to UTC for the query.
    public async Task<List<ClockingStaff>> GetClockingReport(DateTime start, DateTime end)
    {
        var startUtc = OrgClock.ToUtc(start);
        var endUtc = OrgClock.ToUtc(end);

        // include records where clock-in or clock-out falls within the range
        var record = await _db.ClockingsStaff
            .Where(e => (e.ClockInTime >= startUtc && e.ClockInTime <= endUtc) || (e.ClockOutTime != null && e.ClockOutTime >= startUtc && e.ClockOutTime <= endUtc))
            .ToListAsync().ConfigureAwait(false);

        return record;
    }

    public async Task<List<ClockingStaff>> GetClockingReportForStaff(int staffId, DateTime start, DateTime end)
    {
        var startUtc = OrgClock.ToUtc(start);
        var endUtc = OrgClock.ToUtc(end);

        var record = await _db.ClockingsStaff
            .Where(e => e.StafId == staffId && ((e.ClockInTime >= startUtc && e.ClockInTime <= endUtc) || (e.ClockOutTime != null && e.ClockOutTime >= startUtc && e.ClockOutTime <= endUtc)))
            .ToListAsync().ConfigureAwait(false);

        return record;
    }

    public async Task<IEnumerable<ClockingStaff>> GetAll()
    {
        return await _db.ClockingsStaff.ToListAsync().ConfigureAwait(false);
    }

    // start/end, when provided, are org-local wall-clock boundaries; converted to UTC for the query.
    public async Task<PagedResult<ClockingStaff>> GetPaged(int page, int pageSize, int? staffId = null, DateTime? start = null, DateTime? end = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _db.ClockingsStaff.AsQueryable();

        if (staffId.HasValue)
            query = query.Where(e => e.StafId == staffId.Value);

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

        return new PagedResult<ClockingStaff>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IEnumerable<ClockingStaff>> GetAllToday()
    {
        var today = OrgClock.TodayLocalDate();
        return await _db.ClockingsStaff.Where(e => e.ClockDate == today).ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<ClockingStaff>> GetAllById(long id)
    {
        return await _db.ClockingsStaff
            .AsNoTrackingWithIdentityResolution()
            .Where(e => e.StafId == id).ToListAsync().ConfigureAwait(false);
    }

    // Single canonical lookup for "this staff member's session for today, if any" - used both to
    // decide whether a new clock-in is allowed and to find the record clock-out/break actions
    // should mutate, so there's exactly one place that defines what "today's session" means.
    public async Task<ClockingStaff?> GetTodayRecord(long staffId)
    {
        var today = OrgClock.TodayLocalDate();
        return await _db.ClockingsStaff
            .FirstOrDefaultAsync(e => e.StafId == staffId && e.ClockDate == today)
            .ConfigureAwait(false);
    }

    public async Task<bool> ClockOut(ClockingStaff model)
    {
        var item = await _db.ClockingsStaff.FindAsync(model.ClockingStaffId).ConfigureAwait(false);
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

        _db.ClockingsStaff.Update(item);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> BreakStart(ClockingStaff model)
    {
        var item = await _db.ClockingsStaff.FindAsync(model.ClockingStaffId).ConfigureAwait(false);
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

        _db.ClockingsStaff.Update(item);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> BreakEnd(ClockingStaff model)
    {
        var item = await _db.ClockingsStaff.FindAsync(model.ClockingStaffId).ConfigureAwait(false);
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

        _db.ClockingsStaff.Update(item);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> CheckToday(ClockingStaff model)
    {
        var today = OrgClock.TodayLocalDate();
        return await _db.ClockingsStaff
            .AnyAsync(e => e.StafId == model.StafId && e.ClockDate == today)
            .ConfigureAwait(false);
    }

    public async Task<ClockingStaff> Create(ClockingStaff model)
    {
        try
        {
            model.ClockDate = OrgClock.TodayLocalDate();

            var exists = await _db.ClockingsStaff
                .AnyAsync(e => e.StafId == model.StafId && e.ClockDate == model.ClockDate)
                .ConfigureAwait(false);
            if (exists)
            {
                return null!;
            }

            await _db.ClockingsStaff.AddAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (DbUpdateException ex) when (IsDuplicateClockingViolation(ex))
        {
            // Lost a race with a concurrent clock-in for the same staff member/day. The unique
            // index on (StafId, ClockDate) is the real guarantee here; the check above is just
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

    public async Task<bool> CreateBatch(List<ClockingStaff> model)
    {
        try
        {
            await _db.ClockingsStaff.AddRangeAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<bool> DeleteBatch(List<ClockingStaff> model)
    {
        try
        {
            _db.ClockingsStaff.RemoveRange(model);
            await _db.SaveChangesAsync();

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<bool> Delete(ClockingStaff model)
    {
        try
        {

            _db.ClockingsStaff.Remove(model);
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
