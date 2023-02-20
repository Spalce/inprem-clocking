using InpremClockingApp.Data;
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

    public async Task<IEnumerable<ClockingStaff>> GetAll()
    {
        return await _db.ClockingsStaff.ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<ClockingStaff>> GetAllToday()
    {
        return await _db.ClockingsStaff.Where(e => e.CreatedAt!.Value.Date == DateTime.Today.Date).ToListAsync().ConfigureAwait(false);
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
            item!.ClockOutTime = DateTime.Now;
            if (item.LeaveOnBreakTime != null)
            {
                item.ReturnOnBreakTime = DateTime.Now;
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
                item!.LeaveOnBreakTime = DateTime.Now;
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
                item!.ReturnOnBreakTime = DateTime.Now;
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
        var item = await _db.ClockingsStaff
            .FirstOrDefaultAsync(e => e.StafId == model.StafId && e.CreatedAt!.Value.Date == DateTime.Now.Date).ConfigureAwait(false);
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
            var check = await _db.ClockingsStaff
                .FirstOrDefaultAsync(e => e.StafId == model.StafId && e.CreatedAt!.Value.Date == DateTime.Now.Date)
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
