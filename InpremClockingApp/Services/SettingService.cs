using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class SettingService
{
    private readonly ApplicationDbContext _db;

    public SettingService(ApplicationDbContext db)
    {
        _db = db;
    }

    /*public async Task<Ie> GetSettingAsync()
    {
        var records = await _db.Setting.ToListAsync();
        if (records != null!)
        {
            return records
        }

        return null!;
    }*/

    public async Task<IEnumerable<Setting>> GetAllAsync()
    {
        var records = await _db.Setting.ToListAsync();
        if (records != null!)
        {
            return records!;
        }

        return null!;
    }

    public async Task<Setting> GetByIdAsync(int id)
    {
        var record = await _db.Setting.FindAsync(id);
        if (record != null!)
        {
            return record!;
        }

        return null!;
    }

    public async Task<Setting> Create(Setting model)
    {
        try
        {
            await _db.Setting.AddAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<Setting> Update(Setting model)
    {
        try
        {
            var item = await _db.Setting.FindAsync(model.Id).ConfigureAwait(false);
            if (item == null!)
                return null!;

            item.Action = model.Action;
            item.Duration = model.Duration;
            item.Name = model.Name;

            _db.Setting.Update(item);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
