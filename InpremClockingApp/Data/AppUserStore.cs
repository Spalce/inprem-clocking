using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Data;

/// <summary>
/// Identity's user store, with the one deliberate exception to the fail-closed AppUser query
/// filter (multi-tenancy Phase 0 step 0.5): looking an account up by id, user name or email -
/// what sign-in, password reset, the security-stamp check and invitations need - sees every
/// account, because those lookups happen before (or regardless of) an organization being chosen.
/// Listing accounts (<c>UserManager.Users</c>) stays filtered to the current organization's members.
/// Every unfiltered account lookup in the app should go through here (or the user validator).
/// </summary>
public sealed class AppUserStore : UserStore<AppUser, AppRole, ApplicationDbContext>
{
    public AppUserStore(ApplicationDbContext context, IdentityErrorDescriber? describer = null)
        : base(context, describer)
    {
    }

    private IQueryable<AppUser> AllAccounts => Context.Users.IgnoreQueryFilters();

    public override Task<AppUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return AllAccounts.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public override Task<AppUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return AllAccounts.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);
    }

    public override Task<AppUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return AllAccounts.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    protected override Task<AppUser?> FindUserAsync(string userId, CancellationToken cancellationToken) =>
        FindByIdAsync(userId, cancellationToken);
}
