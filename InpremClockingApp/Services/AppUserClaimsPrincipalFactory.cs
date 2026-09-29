using System.Security.Claims;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Services;

// Stamps the signed-in user's TenantId onto their auth cookie as a claim at sign-in, so every
// later request can resolve "which tenant is this for" (CurrentTenantService) without a DB
// lookup. See multi-tenancy.md decision #5. No claim is added for a user with no TenantId
// (reserved for a future tenant-less SuperAdmin role).
public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, AppRole>
{
    public AppUserClaimsPrincipalFactory(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    public override async Task<ClaimsPrincipal> CreateAsync(AppUser user)
    {
        var principal = await base.CreateAsync(user);

        if (user.TenantId.HasValue)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaim(
                new Claim(TenantClaimTypes.TenantId, user.TenantId.Value.ToString()));
        }

        return principal;
    }
}
