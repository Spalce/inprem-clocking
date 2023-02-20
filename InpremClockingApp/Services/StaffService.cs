using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class StaffService
{
    private readonly ApplicationDbContext _db;

    public StaffService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Staff>> GetAll()
    {
        return await _db.Staffs.OrderBy(e => e.FirstName).ToListAsync().ConfigureAwait(false);
    }

    public async Task<Staff> GetById(long id)
    {
        return await _db.Staffs.FindAsync(id).ConfigureAwait(false)!;
    }

    public async Task<Staff> GetByEmail(string email)
    {
        return await _db.Staffs.FirstOrDefaultAsync(e => e.EmailAddress == email).ConfigureAwait(false)!;
    }

    public async Task<Staff> Create(Staff model)
    {
        try
        {
            await _db.Staffs.AddAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<Staff> Update(Staff model)
    {
        try
        {
            var item = await _db.Staffs.FindAsync(model.StaffId).ConfigureAwait(false);
            if (item == null!)
                return null!;

            item.FirstName = model.FirstName;
            item.LastName = model.LastName;
            item.EmailAddress = model.EmailAddress;
            item.ZipCode = model.ZipCode;
            item.Gender = model.Gender;
            item.PhoneNumber = model.PhoneNumber;
            item.Address = model.Address;

            _db.Staffs.Update(item);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<bool> Delete(long id)
    {
        try
        {
            var item = await _db.Staffs.FindAsync(id).ConfigureAwait(false);
            if (item == null!)
                return false!;

            _db.Staffs.Remove(item);
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
