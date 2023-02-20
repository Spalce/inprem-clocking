using InpremClockingApp.Data;
using InpremClockingApp.Models;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Services;

public class VolunteerService
{
    private readonly ApplicationDbContext _db;

    public VolunteerService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Volunteer>> GetAll()
    {
        return await _db.Volunteers.OrderBy(e => e.FirstName).ToListAsync().ConfigureAwait(false);
    }

    public async Task<Volunteer> GetById(long id)
    {
        return await _db.Volunteers.FindAsync(id).ConfigureAwait(false)!;
    }

    public async Task<Staff> GetByEmail(string email)
    {
        return await _db.Staffs.FirstOrDefaultAsync(e => e.EmailAddress == email).ConfigureAwait(false)!;
    }

    public async Task<Volunteer> Create(Volunteer model)
    {
        try
        {
            await _db.Volunteers.AddAsync(model).ConfigureAwait(false);
            await _db.SaveChangesAsync();

            return model;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task<Volunteer> Update(Volunteer model)
    {
        try
        {
            var item = await _db.Volunteers.FindAsync(model.VolunteerId).ConfigureAwait(false);
            if (item == null!)
                return null!;

            item.FirstName = model.FirstName;
            item.LastName = model.LastName;
            item.EmailAddress = model.EmailAddress;
            item.ZipCode = model.ZipCode;
            item.Gender = model.Gender;
            item.PhoneNumber = model.PhoneNumber;
            item.Address = model.Address;

            _db.Volunteers.Update(item);
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
