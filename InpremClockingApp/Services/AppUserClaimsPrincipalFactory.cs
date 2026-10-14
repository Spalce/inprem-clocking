using System.Security.Claims;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Services;

// Stamps the signed-in user's organization onto their auth cookie at sign-in (C2-7, multi-tenancy
// Phase 0 step 0.3): tenant_id and membership_id now come from the user's Active membership
// (platform.TenantMemberships), not from AppUser.TenantId. With exactly one Active membership
// nothing changes for the user. With several, no organization is stamped here: the
// "Choose organization" page signs them in again with the one they pick. With none (e.g. the
// membership was revoked), no organization is stamped and TenantContextMiddleware signs them out
// on their next request. Platform operators (SuperAdmin, no organization) get no tenant claim,
// as before.
public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, AppRole>
{
    private readonly IMembershipService _memberships;

    public AppUserClaimsPrincipalFactory(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        IOptions<IdentityOptions> options,
        IMembershipService memberships)
        : base(userManager, roleManager, options)
    {
        _memberships = memberships;
    }

    public override async Task<ClaimsPrincipal> CreateAsync(AppUser user)
    {
        var principal = await base.CreateAsync(user);

        var active = await _memberships.GetForSignInAsync(user.Id, user.TenantId);
        if (active.Count == 1)
        {
            var identity = (ClaimsIdentity)principal.Identity!;
            identity.AddClaim(new Claim(TenantClaimTypes.TenantId, active[0].TenantId.ToString()));
            identity.AddClaim(new Claim(PlatformClaimTypes.MembershipId, active[0].MembershipId.ToString()));
            await _memberships.RecordSignInAsync(active[0].MembershipId);
        }

        return principal;
    }
}
