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

    public async Task<PagedResult<Volunteer>> SearchByName(string? q, int page = 1, int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = _db.Volunteers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Replace("%", "\\%") + "%";
            query = query.Where(e => EF.Functions.Like((e.FirstName ?? "") + " " + (e.LastName ?? ""), pattern)
                                     || EF.Functions.Like(e.EmailAddress ?? "", pattern));
        }

        var total = await query.CountAsync().ConfigureAwait(false);
        var items = await query.OrderBy(e => e.FirstName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync().ConfigureAwait(false);

        return new PagedResult<Volunteer>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
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
            // Ensure required fields are present before inserting to avoid DB exceptions
            if (string.IsNullOrWhiteSpace(model.EmailAddress))
                throw new ArgumentException("Email address is required", nameof(model.EmailAddress));

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
