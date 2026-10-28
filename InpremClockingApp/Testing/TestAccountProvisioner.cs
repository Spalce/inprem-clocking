using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Testing;

public sealed class TestAccountProvisioner
{
    private readonly UserManager<AppUser> _users;
    private readonly RoleManager<AppRole> _roles;
    private readonly PlatformDbContext _platform;
    private readonly OrganizationService _organizations;
    private readonly IMembershipService _memberships;

    public TestAccountProvisioner(UserManager<AppUser> users, RoleManager<AppRole> roles, PlatformDbContext platform,
        OrganizationService organizations, IMembershipService memberships)
    {
        _users = users;
        _roles = roles;
        _platform = platform;
        _organizations = organizations;
        _memberships = memberships;
    }

    public async Task<ProvisionResult> CreateProviderAsync(string email, string password, ProviderRole role, CancellationToken ct)
    {
        var user = await CreateUserAsync(email, password, null, ct);
        var provider = ProviderUser.Invite(user.Id, role, DateTime.UtcNow);
        provider.Activate();
        _platform.ProviderUsers.Add(provider);
        await _platform.SaveChangesAsync(ct);
        return new ProvisionResult(user.Id, null, "Provider account created and activated.");
    }

    public async Task<ProvisionResult> CreateOrganizationOwnerAsync(NewOrganization input, string password, string actor, CancellationToken ct)
    {
        var result = await _organizations.CreateAsync(input, actor, null, ct, sendEmail: false);
        if (!result.Succeeded || result.TenantId is not { } tenantId)
            throw new InvalidOperationException(result.Error ?? "The organization could not be created.");

        var user = await _users.FindByEmailAsync(input.OwnerEmail.Trim())
            ?? throw new InvalidOperationException("The owner account was not created.");
        await SetPasswordAsync(user, password, ct);
        user.EmailConfirmed = true;
        await AddRoleAsync(user, IdentitySeeder.AdminRole, ct);
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
            throw new InvalidOperationException(string.Join(" ", updated.Errors.Select(e => e.Description)));
        return new ProvisionResult(user.Id, tenantId, "Organization and owner account created and activated.");
    }

    public async Task<ProvisionResult> CreateOrganizationAdminAsync(int tenantId, string email, string password,
        string firstName, string lastName, string actor, CancellationToken ct)
    {
        if (!await _platform.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            throw new InvalidOperationException("The organization does not exist.");

        var user = await CreateUserAsync(email, password, tenantId, ct, firstName, lastName);
        await AddRoleAsync(user, IdentitySeeder.AdminRole, ct);
        await _memberships.AddMemberAsync(user.Id, tenantId, MembershipRole.Admin, ct);
        return new ProvisionResult(user.Id, tenantId, $"Organization administrator created for organization {tenantId}.");
    }

    private async Task<AppUser> CreateUserAsync(string email, string password, int? tenantId, CancellationToken ct,
        string? firstName = null, string? lastName = null)
    {
        email = email.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256)
            throw new InvalidOperationException("Enter a valid unique username/email.");
        if (await _users.FindByEmailAsync(email) != null)
            throw new InvalidOperationException("An account with that username/email already exists.");

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            TenantId = tenantId,
            FirstName = firstName?.Trim(),
            LastName = lastName?.Trim(),
            Type = IdentitySeeder.AdminRole,
        };
        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task SetPasswordAsync(AppUser user, string password, CancellationToken ct)
    {
        var result = await _users.AddPasswordAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private async Task AddRoleAsync(AppUser user, string role, CancellationToken ct)
    {
        if (!await _roles.RoleExistsAsync(role))
            await _roles.CreateAsync(new AppRole { Name = role, Description = "Full back-office access" });
        var result = await _users.AddToRoleAsync(user, role);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}

public sealed record ProvisionResult(string UserId, int? TenantId, string Message);
