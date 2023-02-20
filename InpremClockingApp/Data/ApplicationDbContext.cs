using InpremClockingApp.Models;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<AppUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Clocking> Clockings { get; set; } = null!;
        public virtual DbSet<ClockingStaff> ClockingsStaff { get; set; } = null!;
        public virtual DbSet<Staff> Staffs { get; set; } = null!;
        public virtual DbSet<Volunteer> Volunteers { get; set; } = null!;
        public virtual DbSet<Setting> Setting { get; set; } = null!;
    }
}