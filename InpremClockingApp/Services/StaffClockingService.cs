using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
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
        var (dayStart, dayEnd) = OrgClock.TodayRangeUtc();
        return await _db.ClockingsStaff.Where(e => e.CreatedAt >= dayStart && e.CreatedAt < dayEnd).ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<ClockingStaff>> GetAllById(long id)
    {
        return await _db.ClockingsStaff
            .AsNoTrackingWithIdentityResolution()
            .Where(e => e.StafId == id).ToListAsync().ConfigureAwait(false);
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
            item.ClockOutTime = now;

            if (item.LeaveOnBreakTime != null &&
                item.ReturnOnBreakTime == null)
            {
                item.ReturnOnBreakTime = now;
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
        var (dayStart, dayEnd) = OrgClock.TodayRangeUtc();
        var item = await _db.ClockingsStaff
            .FirstOrDefaultAsync(e => e.StafId == model.StafId && e.CreatedAt >= dayStart && e.CreatedAt < dayEnd).ConfigureAwait(false);
        if (item == null!)
        {
            return false;
        }

        return true;
    }

    public async Task<ClockingStaff> Create(ClockingStaff model)
    {
        try
        {
            var (dayStart, dayEnd) = OrgClock.TodayRangeUtc();
            var check = await _db.ClockingsStaff
                .FirstOrDefaultAsync(e => e.StafId == model.StafId && e.CreatedAt >= dayStart && e.CreatedAt < dayEnd)
                .ConfigureAwait(false);
            if (check != null!)
            {
                return null!;
            }

            await _db.ClockingsStaff.AddAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
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
