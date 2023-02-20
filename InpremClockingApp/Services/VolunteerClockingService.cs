using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class VolunteerClockingService
{
    private readonly ApplicationDbContext _db;

    public VolunteerClockingService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Clocking>> GetAll()
    {
        return await _db.Clockings.ToListAsync().ConfigureAwait(false);
    }

    public async Task<IEnumerable<Clocking>> GetAllToday()
    {
        return await _db.Clockings.Where(e => e.CreatedAt!.Value.Date == DateTime.Today.Date).ToListAsync().ConfigureAwait(false);
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

        _db.Clockings.Update(item);
        await _db.SaveChangesAsync();

        return true;
    }
    public async Task<bool> CheckToday(Clocking model)
    {
        var item = await _db.Clockings
            .FirstOrDefaultAsync(e => e.VoluntId == model.VoluntId && e.CreatedAt!.Value.Date == DateTime.Now.Date).ConfigureAwait(false);
        if (item == null!)
        {
            return false;
        }

        return true;
    }

    public async Task<Clocking> Create(Clocking model)
    {
        try
        {
            var check = await _db.Clockings
                .FirstOrDefaultAsync(e => e.VoluntId == model.VoluntId && e.CreatedAt!.Value.Date == DateTime.Now.Date)
                .ConfigureAwait(false);
            if (check != null!)
            {
                return null!;
            }

            await _db.Clockings.AddAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
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
