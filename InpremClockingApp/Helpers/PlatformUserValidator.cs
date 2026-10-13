using InpremClockingApp.Data;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Helpers;

// C2-10 (multi-tenancy Phase 0 step 0.4): replaces TenantAwareUserValidator. An email - which is
// also the user name everywhere in this app - belongs to one account platform-wide (decision D2);
// someone who works for two organizations has one account with two memberships
// (platform.TenantMemberships). Backed by the unique index on NormalizedEmail (migration M3).
//
// Same format checks and error messages as Identity's default UserValidator, but the uniqueness
// checks look past the AppUser query filter (IgnoreQueryFilters), which would otherwise hide
// accounts of other organizations from a signed-in admin.
public class PlatformUserValidator : IUserValidator<AppUser>
{
    private readonly ApplicationDbContext _db;

    public PlatformUserValidator(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
    {
        var errors = new List<IdentityError>();
        var userName = await manager.GetUserNameAsync(user).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(userName))
        {
            errors.Add(manager.ErrorDescriber.InvalidUserName(userName));
            return IdentityResult.Failed(errors.ToArray());
        }

        var allowedChars = manager.Options.User.AllowedUserNameCharacters;
        if (!string.IsNullOrEmpty(allowedChars) && userName.Any(c => !allowedChars.Contains(c)))
        {
            errors.Add(manager.ErrorDescriber.InvalidUserName(userName));
            return IdentityResult.Failed(errors.ToArray());
        }

        var normalizedUserName = manager.NormalizeName(userName);
        if (await _db.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.NormalizedUserName == normalizedUserName && u.Id != user.Id).ConfigureAwait(false))
        {
            errors.Add(manager.ErrorDescriber.DuplicateUserName(userName));
        }

        var email = await manager.GetEmailAsync(user).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = manager.NormalizeEmail(email);
            if (await _db.Users.IgnoreQueryFilters()
                    .AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != user.Id).ConfigureAwait(false)
                && !errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                errors.Add(manager.ErrorDescriber.DuplicateEmail(email));
            }
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }
}
