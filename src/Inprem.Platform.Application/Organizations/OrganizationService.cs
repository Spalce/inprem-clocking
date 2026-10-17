using System.Net.Mail;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Application.Time;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Organizations;

namespace Inprem.Platform.Application.Organizations;

/// <summary>
/// Organizations in the Provider Console (Phase 1 step 1.2): list, wizard, detail actions. Every
/// action writes exactly one audit entry (with the organization's id), saved with the change.
/// </summary>
public sealed class OrganizationService
{
    private readonly IOrganizationStore _store;
    private readonly IPlanStore _plans;
    private readonly IAccountGateway _accounts;
    private readonly IProviderStore _provider;
    private readonly IOrganizationMail _mail;
    private readonly IMembershipService _memberships;
    private readonly IClock _clock;

    public OrganizationService(IOrganizationStore store, IPlanStore plans, IAccountGateway accounts, IProviderStore provider,
        IOrganizationMail mail, IMembershipService memberships, IClock clock)
    {
        _store = store;
        _plans = plans;
        _accounts = accounts;
        _provider = provider;
        _mail = mail;
        _memberships = memberships;
        _clock = clock;
    }

    // ---------------------------------------------------------------- reading

    public async Task<OrganizationPage> ListAsync(OrganizationQuery query, CancellationToken ct = default)
    {
        IEnumerable<OrganizationSummary> rows = await _store.ListAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.Slug.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.OwnerEmails.Any(e => e.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
        if (query.Status != null) rows = rows.Where(r => r.Status == query.Status);
        if (query.PlanId != null) rows = rows.Where(r => r.PlanId == query.PlanId);
        if (query.CreatedFrom != null) rows = rows.Where(r => r.CreatedUtc >= query.CreatedFrom.Value);
        if (query.CreatedTo != null) rows = rows.Where(r => r.CreatedUtc < query.CreatedTo.Value.AddDays(1));

        var all = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var size = Math.Clamp(query.PageSize, 1, 200);
        var page = Math.Max(1, query.Page);
        return new OrganizationPage(all.Skip((page - 1) * size).Take(size).ToList(), all.Count, page, size);
    }

    public Task<OrganizationDetail?> GetAsync(int tenantId, CancellationToken ct = default) => _store.GetAsync(tenantId, ct);

    public async Task<SlugCheck> CheckSlugAsync(string? slug, int? exceptTenantId = null, CancellationToken ct = default)
    {
        var value = (slug ?? "").Trim().ToLowerInvariant();
        var problem = TenantProfile.SlugProblem(value);
        if (problem == null && await _store.SlugTakenAsync(value, exceptTenantId, ct).ConfigureAwait(false)) problem = $"\"{value}\" is already used.";
        return new SlugCheck(problem == null, problem, problem == null ? value : await SuggestAsync(value.Length > 0 ? value : "org", exceptTenantId, ct).ConfigureAwait(false));
    }

    private async Task<string> SuggestAsync(string start, int? exceptTenantId, CancellationToken ct)
    {
        var baseSlug = TenantProfile.Suggest(start);
        if (TenantProfile.SlugProblem(baseSlug) == null && !await _store.SlugTakenAsync(baseSlug, exceptTenantId, ct).ConfigureAwait(false)) return baseSlug;
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{baseSlug[..Math.Min(baseSlug.Length, 45)]}-{n}";
            if (!await _store.SlugTakenAsync(candidate, exceptTenantId, ct).ConfigureAwait(false)) return candidate;
        }
        return $"{baseSlug[..Math.Min(baseSlug.Length, 40)]}-{Guid.NewGuid():N}"[..50];
    }

    // ---------------------------------------------------------------- wizard checks (one per step)

    public async Task<IReadOnlyList<string>> CheckOrganizationStepAsync(string name, string slug, string timeZoneId, string? address, string? contact, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) errors.Add("Give the organization's name (up to 200 characters).");
        var slugCheck = await CheckSlugAsync(slug, null, ct).ConfigureAwait(false);
        if (!slugCheck.Available) errors.Add(slugCheck.Problem!);
        if (TimeZoneResolver.TryFind(timeZoneId) == null) errors.Add("Choose a time zone.");
        if (address?.Trim().Length > 255) errors.Add("The address is too long (255 characters at most).");
        if (contact?.Trim().Length > 255) errors.Add("The contact details are too long (255 characters at most).");
        return errors;
    }

    public async Task<IReadOnlyList<string>> CheckOwnerStepAsync(string firstName, string lastName, string email, string? mobile, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length > 100) errors.Add("Give the owner's first name.");
        if (string.IsNullOrWhiteSpace(lastName) || lastName.Trim().Length > 100) errors.Add("Give the owner's last name.");
        if (!IsValidEmail(email?.Trim() ?? "")) errors.Add("Give the owner's email address.");
        else if (await _accounts.FindByEmailAsync(email!.Trim(), ct).ConfigureAwait(false) is { } account
                 && await _provider.FindProviderUserAsync(account.Id, ct).ConfigureAwait(false) != null)
            errors.Add("That email belongs to a provider account. Use the organization's own address.");
        if (!string.IsNullOrWhiteSpace(mobile) && (mobile.Trim().Length > 20 || mobile.Count(char.IsDigit) < 7)) errors.Add("The mobile number does not look right.");
        return errors;
    }

    public async Task<IReadOnlyList<string>> CheckBillingStepAsync(Guid? planId, Guid? priceId, BillingMode mode, int trialDays, CancellationToken ct = default)
    {
        try
        {
            var (plan, price) = await FindPlanAsync(planId, priceId, ct).ConfigureAwait(false);
            Subscription.Start(0, plan, price, mode, trialDays, _clock.UtcNow);
            return Array.Empty<string>();
        }
        catch (ArgumentException e)
        {
            return new[] { Message(e) };
        }
    }

    /// <summary>Whether the owner email already has an account (the review step says what will happen).</summary>
    public async Task<bool> OwnerHasAccountAsync(string email, CancellationToken ct = default) =>
        await _accounts.FindByEmailAsync(email.Trim(), ct).ConfigureAwait(false) != null;

    // ---------------------------------------------------------------- create

    public async Task<OrganizationResult> CreateAsync(NewOrganization input, string actor, string? appLinkBase, CancellationToken ct = default)
    {
        var errors = new List<string>();
        errors.AddRange(await CheckOrganizationStepAsync(input.Name, input.Slug, input.TimeZoneId, input.Address, input.ContactInfo, ct).ConfigureAwait(false));
        errors.AddRange(await CheckOwnerStepAsync(input.OwnerFirstName, input.OwnerLastName, input.OwnerEmail, input.OwnerMobile, ct).ConfigureAwait(false));
        errors.AddRange(await CheckBillingStepAsync(input.PlanId, input.PlanPriceId, input.Mode, input.TrialDays, ct).ConfigureAwait(false));
        if (!_mail.CanBuildLinks(appLinkBase)) errors.Add("Links:AppBaseUrl is not configured, so the owner's invitation link cannot be built.");
        if (errors.Count > 0) return OrganizationResult.Fail(string.Join(" ", errors));

        var now = _clock.UtcNow;
        var (plan, price) = await FindPlanAsync(input.PlanId, input.PlanPriceId, ct).ConfigureAwait(false);
        var normalized = input with
        {
            Name = input.Name.Trim(), Slug = input.Slug.Trim().ToLowerInvariant(), Address = Blank(input.Address), ContactInfo = Blank(input.ContactInfo),
            OwnerFirstName = input.OwnerFirstName.Trim(), OwnerLastName = input.OwnerLastName.Trim(), OwnerEmail = input.OwnerEmail.Trim(), OwnerMobile = Blank(input.OwnerMobile),
        };
        var existing = await _accounts.FindByEmailAsync(normalized.OwnerEmail, ct).ConfigureAwait(false);
        var subscription = Subscription.Start(0, plan, price, input.Mode, input.TrialDays, now);

        ProvisionedOrganization created;
        try
        {
            created = await _store.ProvisionAsync(new OrganizationProvisioning(normalized, existing?.Id, subscription, actor, now), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The store's transaction rolled back: nothing of the organization exists.
            return OrganizationResult.Fail($"The organization could not be created, and nothing was saved ({e.GetType().Name}: {e.Message}). Try again; if it keeps failing, check the logs.");
        }

        // After the commit: the email may fail without undoing the organization (Resend is on the Admins tab).
        try
        {
            if (created.OwnerIsNewAccount) await _mail.SendOwnerWelcomeAsync(created.OwnerUserId, normalized.Name, appLinkBase, ct).ConfigureAwait(false);
            else await _mail.SendOwnerInvitationAsync(created.OwnerUserId, normalized.Name, appLinkBase, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await AuditAsync(actor, "organization.invitation-failed", created.TenantId, $"Owner email to {normalized.OwnerEmail} could not be sent ({e.GetType().Name}).", ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return OrganizationResult.Ok(created.TenantId, $"The organization was created, but the email to {normalized.OwnerEmail} could not be sent. Use \"Resend invitation\" on the Admins tab once email works.");
        }

        return OrganizationResult.Ok(created.TenantId);
    }

    // ---------------------------------------------------------------- detail actions

    public async Task<OrganizationResult> UpdateAsync(int tenantId, OrganizationEdit edit, string actor, CancellationToken ct = default)
    {
        if (await _store.GetAsync(tenantId, ct).ConfigureAwait(false) is not { } current) return OrganizationResult.Fail("No such organization.");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(edit.Name) || edit.Name.Trim().Length > 200) errors.Add("Give the organization's name (up to 200 characters).");
        var slug = await CheckSlugAsync(edit.Slug, tenantId, ct).ConfigureAwait(false);
        if (!slug.Available) errors.Add(slug.Problem!);
        if (TimeZoneResolver.TryFind(edit.TimeZoneId) == null) errors.Add("Choose a time zone.");
        if (edit.Address?.Trim().Length > 255 || edit.ContactInfo?.Trim().Length > 255) errors.Add("Address and contact details are 255 characters at most.");
        if (errors.Count > 0) return OrganizationResult.Fail(string.Join(" ", errors));

        var clean = new OrganizationEdit(edit.Name.Trim(), edit.Slug.Trim().ToLowerInvariant(), edit.TimeZoneId, Blank(edit.Address), Blank(edit.ContactInfo));
        var changes = new List<string>();
        if (clean.Name != current.Summary.Name) changes.Add($"name \"{current.Summary.Name}\" → \"{clean.Name}\"");
        if (clean.Slug != current.Summary.Slug) changes.Add($"slug {current.Summary.Slug} → {clean.Slug}");
        if (clean.TimeZoneId != current.TimeZoneId) changes.Add($"time zone {current.TimeZoneId} → {clean.TimeZoneId}");
        if (clean.Address != current.Address) changes.Add("address");
        if (clean.ContactInfo != current.ContactInfo) changes.Add("contact");
        if (changes.Count == 0) return OrganizationResult.Ok(tenantId);

        await _store.UpdateAsync(tenantId, clean, Audit(actor, "organization.updated", tenantId, "Changed: " + string.Join(", ", changes)), ct).ConfigureAwait(false);
        return OrganizationResult.Ok(tenantId);
    }

    /// <summary>
    /// Suspends the organization (read-only, C3-3) and emails its active owners. The reason is
    /// always in the audit log; it goes into the email only when <paramref name="shareReason"/>.
    /// </summary>
    public async Task<OrganizationResult> SuspendAsync(int tenantId, string reason, string actor, CancellationToken ct = default, bool shareReason = false)
    {
        var result = await ChangeProfileAsync(tenantId, actor, "organization.suspended",
            p => { p.Suspend(reason, _clock.UtcNow); return $"Reason: {reason.Trim()}{(shareReason ? " (shared with the owners)" : "")}"; }, ct).ConfigureAwait(false);
        return result.Succeeded ? await TellOwnersAsync(tenantId, suspended: true, shareReason ? reason.Trim() : null, ct).ConfigureAwait(false) : result;
    }

    /// <summary>Reactivates the organization (full access again) and emails its active owners.</summary>
    public async Task<OrganizationResult> ReactivateAsync(int tenantId, string reason, string actor, CancellationToken ct = default)
    {
        var result = await ChangeProfileAsync(tenantId, actor, "organization.reactivated", p => { p.Reactivate(reason, _clock.UtcNow); return $"Reason: {reason.Trim()}"; }, ct).ConfigureAwait(false);
        return result.Succeeded ? await TellOwnersAsync(tenantId, suspended: false, null, ct).ConfigureAwait(false) : result;
    }

    /// <summary>Emails the active owners; a failure is audited and reported as a warning (the change stands).</summary>
    private async Task<OrganizationResult> TellOwnersAsync(int tenantId, bool suspended, string? reason, CancellationToken ct)
    {
        var detail = await _store.GetAsync(tenantId, ct).ConfigureAwait(false);
        if (detail == null) return OrganizationResult.Ok(tenantId);
        var owners = detail.Admins.Where(a => a.Role == MembershipRole.Owner && a.Status == MembershipStatus.Active && !string.IsNullOrWhiteSpace(a.Email)).ToList();
        var failed = new List<string>();
        foreach (var owner in owners)
        {
            try
            {
                await _mail.SendStatusChangedAsync(owner.Email, detail.Summary.Name, suspended, reason, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed.Add(owner.Email);
            }
        }
        if (failed.Count > 0)
        {
            await AuditAsync("system", "notification.failed", tenantId, $"Organization {(suspended ? "suspended" : "reactivated")} email not delivered to: {string.Join(", ", failed)}", ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return owners.Count == 0 ? OrganizationResult.Ok(tenantId, "The organization has no active owner to email.")
            : failed.Count > 0 ? OrganizationResult.Ok(tenantId, "Done, but the email to the owners could not be sent.")
            : OrganizationResult.Ok(tenantId);
    }

    public async Task<OrganizationResult> AddNoteAsync(int tenantId, string text, string actor, CancellationToken ct = default)
    {
        if (await _store.FindProfileAsync(tenantId, ct).ConfigureAwait(false) == null) return OrganizationResult.Fail("No such organization.");
        TenantNote note;
        try
        {
            note = TenantNote.Create(tenantId, text, actor, _clock.UtcNow);
        }
        catch (ArgumentException e)
        {
            return OrganizationResult.Fail(Message(e));
        }
        await _store.AddNoteAsync(note, ct).ConfigureAwait(false);
        await AuditAsync(actor, "organization.note-added", tenantId, note.Text.Length > 200 ? note.Text[..200] + "…" : note.Text, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return OrganizationResult.Ok(tenantId);
    }

    /// <summary>
    /// Sends the right email again: a "set your password" link to an owner or admin who has none
    /// yet, or the invitation reminder for an existing account whose membership is still Invited.
    /// </summary>
    public async Task<OrganizationResult> ResendInvitationAsync(int tenantId, string userId, string actor, string? appLinkBase, CancellationToken ct = default)
    {
        if (await FindAdminAsync(tenantId, userId, ct).ConfigureAwait(false) is not { Admin: { } admin } found) return OrganizationResult.Fail("That person is not an admin of this organization.");
        var detail = found.Detail;
        if (!_mail.CanBuildLinks(appLinkBase)) return OrganizationResult.Fail("Links:AppBaseUrl is not configured, so no link can be built.");

        string what;
        if (!admin.HasPassword)
        {
            await _mail.SendOwnerWelcomeAsync(userId, detail.Summary.Name, appLinkBase, ct).ConfigureAwait(false);
            what = "set-your-password link";
        }
        else if (admin.Status == MembershipStatus.Invited)
        {
            await _mail.SendOwnerInvitationAsync(userId, detail.Summary.Name, appLinkBase, ct).ConfigureAwait(false);
            what = "invitation reminder";
        }
        else
        {
            return OrganizationResult.Fail("This admin has already set a password and accepted. Use \"Send password reset\" if they cannot sign in.");
        }

        await AuditAsync(actor, "organization.invitation-resent", tenantId, $"{what} to {admin.Email}", ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return OrganizationResult.Ok(tenantId);
    }

    public async Task<OrganizationResult> SendPasswordResetAsync(int tenantId, string userId, string actor, string? appLinkBase, CancellationToken ct = default)
    {
        if (await FindAdminAsync(tenantId, userId, ct).ConfigureAwait(false) is not { Admin: { } admin } found) return OrganizationResult.Fail("That person is not an admin of this organization.");
        var detail = found.Detail;
        if (!admin.HasPassword) return OrganizationResult.Fail("This admin has no password yet. Use \"Resend invitation\".");
        if (!_mail.CanBuildLinks(appLinkBase)) return OrganizationResult.Fail("Links:AppBaseUrl is not configured, so no link can be built.");

        await _mail.SendPasswordResetAsync(userId, detail.Summary.Name, appLinkBase, ct).ConfigureAwait(false);
        await AuditAsync(actor, "organization.password-reset-sent", tenantId, $"to {admin.Email}", ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return OrganizationResult.Ok(tenantId);
    }

    /// <summary>Makes an Active admin the Owner; the current owner(s) become admins.</summary>
    public async Task<OrganizationResult> TransferOwnershipAsync(int tenantId, string toUserId, string actor, CancellationToken ct = default)
    {
        var memberships = await _store.ListMembershipsAsync(tenantId, ct).ConfigureAwait(false);
        var target = memberships.FirstOrDefault(m => m.UserId == toUserId);
        if (target == null) return OrganizationResult.Fail("That person is not an admin of this organization.");
        if (target.Status != MembershipStatus.Active) return OrganizationResult.Fail("Only an active admin (signed up and accepted) can become the owner.");
        if (target.Role == MembershipRole.Owner) return OrganizationResult.Fail("That person is already the owner.");

        var previous = memberships.Where(m => m.Role == MembershipRole.Owner && m.UserId != toUserId).ToList();
        foreach (var owner in previous) owner.ChangeRole(MembershipRole.Admin);
        target.ChangeRole(MembershipRole.Owner);

        var targetEmail = (await _accounts.FindByIdAsync(toUserId, ct).ConfigureAwait(false))?.Email ?? toUserId;
        var previousEmails = new List<string>();
        foreach (var owner in previous) previousEmails.Add((await _accounts.FindByIdAsync(owner.UserId, ct).ConfigureAwait(false))?.Email ?? owner.UserId);
        await AuditAsync(actor, "organization.ownership-transferred", tenantId,
            $"New owner {targetEmail}; previous {(previousEmails.Count == 0 ? "none" : string.Join(", ", previousEmails))} now admin", ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var membership in previous.Append(target)) _memberships.Invalidate(membership.Id);
        return OrganizationResult.Ok(tenantId);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<OrganizationResult> ChangeProfileAsync(int tenantId, string actor, string action, Func<TenantProfile, string> change, CancellationToken ct)
    {
        var profile = await _store.FindProfileAsync(tenantId, ct).ConfigureAwait(false);
        if (profile == null) return OrganizationResult.Fail("No such organization.");
        string details;
        try
        {
            details = change(profile);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return OrganizationResult.Fail(Message(e));
        }
        await AuditAsync(actor, action, tenantId, details, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return OrganizationResult.Ok(tenantId);
    }

    private sealed record AdminLookup(OrganizationDetail Detail, AdminView? Admin);

    private async Task<AdminLookup?> FindAdminAsync(int tenantId, string userId, CancellationToken ct)
    {
        var detail = await _store.GetAsync(tenantId, ct).ConfigureAwait(false);
        return detail == null ? null : new AdminLookup(detail, detail.Admins.FirstOrDefault(a => a.UserId == userId));
    }

    private async Task<(Plan? Plan, PlanPrice? Price)> FindPlanAsync(Guid? planId, Guid? priceId, CancellationToken ct)
    {
        if (planId == null) return (null, null);
        var plan = await _plans.FindAsync(planId.Value, ct).ConfigureAwait(false) ?? throw new ArgumentException("That plan does not exist.", nameof(planId));
        var price = priceId == null ? null : plan.Prices.FirstOrDefault(p => p.Id == priceId) ?? throw new ArgumentException("That price does not belong to the plan.", nameof(priceId));
        return (plan, price);
    }

    private Task AuditAsync(string actor, string action, int tenantId, string details, CancellationToken ct) =>
        _store.AddAuditAsync(Audit(actor, action, tenantId, details), ct);

    private AuditEntry Audit(string actor, string action, int tenantId, string details) =>
        AuditEntry.Record(actor, action, tenantId.ToString(), details.Length > 2000 ? details[..2000] : details, _clock.UtcNow, tenantId);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidEmail(string email)
    {
        if (email.Length is 0 or > 256) return false;
        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Message(Exception e) =>
        e is ArgumentException a && a.ParamName != null ? a.Message.Replace($" (Parameter '{a.ParamName}')", "") : e.Message;
}
