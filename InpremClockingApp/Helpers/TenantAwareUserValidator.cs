using InpremClockingApp.Data;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Helpers;

// Replaces ASP.NET Core Identity's default UserValidator<AppUser> (see Program.cs), whose
// built-in username-uniqueness check (manager.FindByNameAsync) is a plain global lookup against
// the whole AspNetUsers table - exactly the kind of cross-tenant leak Staff/Volunteer were
// explicitly redesigned to avoid in multi-tenancy.md Phase 1b. UserName is set equal to the
// admin's email everywhere in this app (AuthService, TenantAdminService), so this is effectively
// "two different organizations can't share an admin's email address" today - this scopes the
// uniqueness check to the user's own TenantId instead, matching the DB-level composite unique
// index added alongside this (ApplicationDbContext). The resulting cross-tenant duplicates are
// disambiguated at sign-in time by password (Login.cshtml.cs) and at password-reset time by
// token validity (ForgotPassword/ResetPassword.cshtml.cs) - see those files for why plain
// FindByNameAsync/FindByEmailAsync can no longer be trusted to resolve a single account.
//
// A SuperAdmin (TenantId null) is still checked for uniqueness among other SuperAdmins only -
// there's no tenant to scope that case to, and it's the one account type genuinely meant to be
// unique platform-wide.
//
// Performs the same format checks the default validator does (RequireUniqueEmail is never
// enabled in this app, so uniqueness is only ever checked via UserName here, same as before).
public class TenantAwareUserValidator : IUserValidator<AppUser>
{
    private readonly ApplicationDbContext _db;

    public TenantAwareUserValidator(ApplicationDbContext db)
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

        // IgnoreQueryFilters(): the AppUser filter already passes through unfiltered for a
        // SuperAdmin caller (TenantId null - see ApplicationDbContext), but this makes the
        // tenant scoping explicit and correct regardless of who happens to be calling.
        var collision = await _db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedUserName == normalizedUserName && u.TenantId == user.TenantId && u.Id != user.Id)
            .ConfigureAwait(false);

        if (collision)
        {
            errors.Add(manager.ErrorDescriber.DuplicateUserName(userName));
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }
}
