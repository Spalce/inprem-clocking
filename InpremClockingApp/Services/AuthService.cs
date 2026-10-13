using System.Security.Claims;
using System.Text.Encodings.Web;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Memberships;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlatformEmailSender = Inprem.Platform.Application.Memberships.IEmailSender;

namespace InpremClockingApp.Services;

/// <summary>What happened when an admin was added on the Admins page.</summary>
public enum AdminAddOutcome
{
    /// <summary>A new account was created and made an Active Admin of the organization.</summary>
    Created,

    /// <summary>The email already had an account: it was invited to the organization instead (C3-6).</summary>
    Invited,

    /// <summary>Nothing was created; see the errors.</summary>
    Failed,
}

public sealed record AdminAddResult(AdminAddOutcome Outcome, IdentityResult Identity);

public class AuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ICurrentTenantService _currentTenant;
    private readonly IMembershipService _memberships;
    private readonly PlatformEmailSender _email;
    private readonly IHttpContextAccessor _http;
    private readonly ICurrentTenantProfile _tenantProfile;

    public AuthService(
        UserManager<AppUser> userManager,
        ICurrentTenantService currentTenant,
        IMembershipService memberships,
        PlatformEmailSender email,
        IHttpContextAccessor http,
        ICurrentTenantProfile tenantProfile)
    {
        _tenantProfile = tenantProfile;
        _userManager = userManager;
        _currentTenant = currentTenant;
        _memberships = memberships;
        _email = email;
        _http = http;
    }

    // C2-6 (multi-tenancy Phase 0 steps 0.4-0.5): the organization's admins are its members -
    // exactly the accounts the fail-closed AppUser query filter lets this organization see (an
    // Active membership, or an account of this organization with no membership row yet).
    // Without an organization the list is empty.
    private Task<IQueryable<AppUser>> MembersQueryAsync() => Task.FromResult(_userManager.Users);

    public async Task<IEnumerable<AppUser>> GetAll()
    {
        return await (await MembersQueryAsync().ConfigureAwait(false)).ToListAsync().ConfigureAwait(false);
    }

    public async Task<Models.PagedResult<AppUser>> GetPaged(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var query = (await MembersQueryAsync().ConfigureAwait(false)).OrderBy(u => u.Email);

        var total = await query.CountAsync().ConfigureAwait(false);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync().ConfigureAwait(false);

        return new Models.PagedResult<AppUser>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Creates a new back-office admin account and grants it the Admin role.
    /// Only reachable from the admin-only /User page - this is the sole way new
    /// admin accounts get created now that public self-registration is closed.
    /// </summary>
    public async Task<IdentityResult> CreateAdmin(string email, string firstName, string lastName, string password) =>
        (await AddAdminAsync(email, firstName, lastName, password).ConfigureAwait(false)).Identity;

    /// <summary>
    /// C2-6 / C3-6 (multi-tenancy Phase 0 step 0.4): adds an admin to the current organization.
    /// <list type="bullet">
    /// <item>A new email: creates the account (with the given password) and an Active Admin membership.</item>
    /// <item>An email that already has an account: no second account; an Invited membership and an
    /// invitation email instead. The password typed in is not used - the person keeps their own.</item>
    /// <item>Already an Active member here: the same "already taken" error as before.</item>
    /// </list>
    /// </summary>
    public async Task<AdminAddResult> AddAdminAsync(string email, string firstName, string lastName, string password)
    {
        var tenantId = _currentTenant.TenantId;
        var existing = await _userManager.FindByEmailAsync(email).ConfigureAwait(false);

        if (existing == null || !tenantId.HasValue)
        {
            return await CreateAccountAsync(email, firstName, lastName, password, tenantId).ConfigureAwait(false);
        }

        var invitedBy = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var outcome = await _memberships.InviteAsync(existing.Id, tenantId.Value, MembershipRole.Admin, invitedBy).ConfigureAwait(false);
        if (outcome == InviteOutcome.AlreadyMember)
        {
            return new AdminAddResult(AdminAddOutcome.Failed, IdentityResult.Failed(_userManager.ErrorDescriber.DuplicateUserName(email)));
        }

        if (!await _userManager.IsInRoleAsync(existing, IdentitySeeder.AdminRole).ConfigureAwait(false))
        {
            await _userManager.AddToRoleAsync(existing, IdentitySeeder.AdminRole).ConfigureAwait(false);
        }

        await SendInvitationAsync(existing.Email ?? email).ConfigureAwait(false);
        return new AdminAddResult(AdminAddOutcome.Invited, IdentityResult.Success);
    }

    private async Task<AdminAddResult> CreateAccountAsync(string email, string firstName, string lastName, string password, int? tenantId)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            Type = IdentitySeeder.AdminRole,
            TenantId = tenantId,
        };

        var result = await _userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new AdminAddResult(AdminAddOutcome.Failed, result);
        }

        await _userManager.AddToRoleAsync(user, IdentitySeeder.AdminRole).ConfigureAwait(false);
        if (tenantId.HasValue)
        {
            // If this fails the account still gets its membership at first sign-in.
            await _memberships.AddMemberAsync(user.Id, tenantId.Value, MembershipRole.Admin).ConfigureAwait(false);
        }
        return new AdminAddResult(AdminAddOutcome.Created, result);
    }

    private Task SendInvitationAsync(string to)
    {
        var request = _http.HttpContext?.Request;
        var link = request == null ? "/Account/Invitations" : $"{request.Scheme}://{request.Host}{request.PathBase}/Account/Invitations";
        var body =
            $"<p>You have been invited to be an administrator of <strong>{HtmlEncoder.Default.Encode(_tenantProfile.Name)}</strong> in Inprem Clockin.</p>" +
            $"<p>Sign in with your existing account and <a href='{HtmlEncoder.Default.Encode(link)}'>accept the invitation</a>.</p>";
        return _email.SendAsync(to, "Invitation to join an organization", body);
    }
}
