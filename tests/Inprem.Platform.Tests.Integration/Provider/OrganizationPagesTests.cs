using System.Net;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Organizations;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>Phase 1 step 1.2: organizations list, wizard (P1-3), detail actions, audit per action (P1-4).</summary>
public class OrganizationPagesTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public OrganizationPagesTests(ProviderFixture app) => _app = app;

    private async Task<(HttpClient Client, string Key)> OwnerAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}"[..16] + "@orgs.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Owner);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return (client, key);
    }

    private async Task<(Guid PlanId, Guid PriceId)> PlanAsync()
    {
        using var scope = _app.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var id = (await plans.CreateAsync($"org-plan-{Guid.NewGuid():N}"[..20], new("Org plan", null, null, null, 21, true), BillingCycle.Monthly, 19m, "USD", "test")).PlanId!.Value;
        var plan = (await plans.FindAsync(id))!;
        return (id, plan.Prices[0].Id);
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    /// <summary>Runs the wizard; returns the final response (redirect to the new organization, or the page with errors).</summary>
    private static async Task<HttpResponseMessage> RunWizardAsync(HttpClient client, string name, string slug, string ownerEmail, string planChoice, string mode, string trialDays)
    {
        // Each step posts the page's own form (hidden values of the other steps included) with this step's fields.
        var step1 = await PostAsync(client, "/Provider/Tenants/New", ("Name", name), ("Slug", slug), ("TimeZoneId", "America/Chicago"),
            ("Address", "1 Test Street"), ("ContactInfo", "555-0100"), ("move", "next"));
        Assert.Contains("wizard-step-2", await BodyAsync(step1));
        var step2 = await PostFromHtmlAsync(client, step1, ("OwnerFirstName", "Ama"), ("OwnerLastName", "Owusu"), ("OwnerEmail", ownerEmail), ("OwnerMobile", "555-0111"), ("move", "next"));
        Assert.Contains("wizard-step-3", await BodyAsync(step2));
        var step3 = await PostFromHtmlAsync(client, step2, ("PlanChoice", planChoice), ("Mode", mode), ("TrialDays", trialDays), ("move", "next"));
        Assert.Contains("wizard-step-4", await BodyAsync(step3));
        return await PostFromHtmlAsync(client, step3, ("move", "create"));
    }

    /// <summary>Posts the wizard form found in a response (the next step's page), overriding some fields.</summary>
    private static async Task<HttpResponseMessage> PostFromHtmlAsync(HttpClient client, HttpResponseMessage page, params (string Name, string Value)[] fields)
    {
        var html = await page.Content.ReadAsStringAsync();
        var form = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html).Forms.First(f => f.GetAttribute("data-role") != "sign-out" && f.Method == "post");
        var values = form.Elements.OfType<AngleSharp.Html.Dom.IHtmlInputElement>()
            .Where(i => i.Type == "hidden" && !string.IsNullOrEmpty(i.Name) && fields.All(f => f.Name != i.Name))
            .Select(i => new KeyValuePair<string, string>(i.Name!, i.Value)).ToList();
        values.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
        return await client.PostAsync("/Provider/Tenants/New", new FormUrlEncodedContent(values));
    }

    private static int IdFrom(string location) => int.Parse(location[(location.LastIndexOf('/') + 1)..].Split('?')[0]);

    [Fact]
    public async Task Organization_Wizard_Creates_Everything_And_The_Owner_Signs_In()
    {
        var (owner, _) = await OwnerAsync();
        var (planId, priceId) = await PlanAsync();
        var slug = Unique("acme");
        var ownerEmail = Unique("ama") + "@acme.test";

        // Live address-name check.
        var check = await (await owner.GetAsync($"/Provider/Tenants/New?handler=Slug&slug={slug}")).Content.ReadAsStringAsync();
        Assert.Contains("\"available\":true", check);
        Assert.Contains("\"available\":false", await (await owner.GetAsync("/Provider/Tenants/New?handler=Slug&slug=admin")).Content.ReadAsStringAsync());

        // A refused step stays on its page with the reason.
        var bad = await PostAsync(owner, "/Provider/Tenants/New", ("Name", "Acme"), ("Slug", "Bad Slug"), ("TimeZoneId", "America/Chicago"), ("move", "next"));
        Assert.Contains("wizard-step-1", await BodyAsync(bad));
        Assert.Contains("lower-case", await BodyAsync(bad));

        var created = await RunWizardAsync(owner, "Acme Community Centre", slug, ownerEmail, $"{planId}|{priceId}", "PayPal", "14");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var tenantId = IdFrom(Location(created));

        // Everything exists.
        await using (var app = _app.CreateDbContext())
        await using (var platform = _app.CreatePlatformDbContext())
        {
            var tenant = await app.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
            Assert.Equal(("Acme Community Centre", "America/Chicago", "1 Test Street"), (tenant.Name, tenant.TimeZoneId, tenant.Address));
            Assert.False(await app.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId)); // no old billing row since Phase 2 step 2.9
            var account = await app.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == ownerEmail);
            Assert.Null(account.PasswordHash);
            Assert.Equal(tenantId, account.TenantId);
            var membership = await platform.TenantMemberships.SingleAsync(m => m.TenantId == tenantId);
            Assert.Equal((account.Id, MembershipRole.Owner, MembershipStatus.Active), (membership.UserId, membership.Role, membership.Status));
            Assert.Equal(slug, (await platform.TenantProfiles.SingleAsync(p => p.TenantId == tenantId)).Slug);
            var subscription = await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal((planId, BillingMode.PayPal, SubscriptionStatus.Trialing), (subscription.PlanId!.Value, subscription.Mode, subscription.Status));
            Assert.Single(await platform.AuditEntries.Where(a => a.TenantId == tenantId && a.Action == "organization.created").ToListAsync());
        }
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True(await users.IsInRoleAsync((await users.FindByEmailAsync(ownerEmail))!, IdentitySeeder.AdminRole));
        }

        // The slug is now taken; the detail page shows the organization.
        Assert.Contains("\"available\":false", await (await owner.GetAsync($"/Provider/Tenants/New?handler=Slug&slug={slug}")).Content.ReadAsStringAsync());
        var detail = await GetDocumentAsync(owner, Location(created));
        Assert.Contains("created", detail.QuerySelector("[data-testid=success]")!.TextContent);

        // P1-3: the owner chooses a password from the email and signs in on the App site.
        var link = LinkSentTo(_app.Emails, ownerEmail, "ResetPassword?code=");
        Assert.StartsWith("/Identity/Account/ResetPassword?code=", link);
        using var newOwner = _app.CreateClient();
        var reset = await PostAsync(newOwner, link, ("Input.Email", ownerEmail), ("Input.Password", StrongPassword), ("Input.ConfirmPassword", StrongPassword));
        Assert.True(reset.StatusCode == HttpStatusCode.Redirect, $"Reset: {(int)reset.StatusCode} {await BodyAsync(reset)}");
        await SignIn.AsAsync(newOwner, ownerEmail, StrongPassword);
        Assert.Equal(HttpStatusCode.OK, (await newOwner.GetAsync("/BackOffice")).StatusCode);

        // The Admins tab now shows the sign-in.
        var admins = await GetDocumentAsync(owner, $"/Provider/Tenants/Details/{tenantId}?tab=admins");
        Assert.DoesNotContain("Never", admins.QuerySelector("[data-testid=admins]")!.TextContent);
    }

    [Fact]
    public async Task Organization_Owner_With_An_Existing_Account_Is_Invited()
    {
        var (owner, _) = await OwnerAsync();
        var existing = _app.Data.Beta.Owner!.Email; // an admin of another organization
        var created = await RunWizardAsync(owner, "Second Home", Unique("second"), existing, "", "Free", "0");
        var tenantId = IdFrom(Location(created));

        await using var platform = _app.CreatePlatformDbContext();
        var membership = await platform.TenantMemberships.SingleAsync(m => m.TenantId == tenantId);
        Assert.Equal((MembershipRole.Owner, MembershipStatus.Invited), (membership.Role, membership.Status));
        Assert.Equal(SubscriptionStatus.Active, (await platform.Subscriptions.SingleAsync(s => s.TenantId == tenantId)).Status);
        Assert.Contains(_app.Emails.To(existing), e => e.Subject.Contains("invited to Second Home"));
    }

    [Fact]
    public async Task Organization_Creation_Failure_Leaves_Nothing_Behind()
    {
        var (owner, _) = await OwnerAsync();
        var ownerEmail = Unique("fail") + "@fail.test";
        var slug = Unique("fail");

        // Make the last write of the transaction fail (the subscription), after the organization,
        // account, membership and profile were written.
        await using (var platform = _app.CreatePlatformDbContext())
        {
            await platform.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER [platform].[TR_Test_FailSubscriptions] ON [platform].[Subscriptions] AFTER INSERT AS THROW 50099, 'Test failure', 1;");
        }

        try
        {
            var failed = await RunWizardAsync(owner, "Should Not Exist", slug, ownerEmail, "", "Free", "0");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
            Assert.Contains("nothing was saved", await BodyAsync(failed));
        }
        finally
        {
            await using var platform = _app.CreatePlatformDbContext();
            await platform.Database.ExecuteSqlRawAsync("DROP TRIGGER [platform].[TR_Test_FailSubscriptions];");
        }

        await using (var app = _app.CreateDbContext())
        {
            Assert.False(await app.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Name == "Should Not Exist"));
            Assert.False(await app.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == ownerEmail));
        }
        await using (var platform = _app.CreatePlatformDbContext())
        {
            Assert.False(await platform.TenantProfiles.AnyAsync(p => p.Slug == slug));
            Assert.False(await platform.AuditEntries.AnyAsync(a => a.Action == "organization.created" && a.Details!.Contains(slug)));
        }
        Assert.Empty(_app.Emails.To(ownerEmail));
    }

    [Fact]
    public async Task Organization_Detail_Actions_Each_Write_One_Audit_Entry()
    {
        var (owner, key) = await OwnerAsync();
        var ownerEmail = Unique("dora") + "@detail.test";
        var created = await RunWizardAsync(owner, "Detail Org", Unique("detail"), ownerEmail, "", "Free", "0");
        var id = IdFrom(Location(created));
        var otherSlug = $"org-{_app.Data.Alpha.Id}"; // Alpha has no profile row yet: its default name still counts as taken

        // Edit: a taken address name is refused; a good edit saves.
        Assert.Contains("already used", await BodyAsync(await PostAsync(owner, $"/Provider/Tenants/Edit/{id}", ("Name", "Detail Org"), ("Slug", otherSlug), ("TimeZoneId", "America/Chicago"))));
        Assert.Equal($"/Provider/Tenants/Details/{id}", Location(await PostAsync(owner, $"/Provider/Tenants/Edit/{id}",
            ("Name", "Detail Org Renamed"), ("Slug", Unique("renamed")), ("TimeZoneId", "America/Denver"), ("Address", "2 Main St"), ("ContactInfo", ""))));

        // Suspend needs a reason; then reactivate.
        Assert.Contains("Give a reason", await BodyAsync(await PostAsync(owner, $"/Provider/Tenants/Suspend/{id}", ("Reason", " "))));
        Assert.Equal($"/Provider/Tenants/Details/{id}", Location(await PostAsync(owner, $"/Provider/Tenants/Suspend/{id}", ("Reason", "Asked to pause"))));
        await using (var platform = _app.CreatePlatformDbContext())
        {
            Assert.Equal(OrganizationStatus.Suspended, (await platform.TenantProfiles.SingleAsync(p => p.TenantId == id)).Status);
        }
        Assert.Equal($"/Provider/Tenants/Details/{id}", Location(await PostAsync(owner, $"/Provider/Tenants/Reactivate/{id}", ("Reason", "Back again"))));

        // Notes.
        Assert.Contains("tab=notes", Location(await PostAsync(owner, $"/Provider/Tenants/AddNote/{id}", ("Text", "Called the owner about training."))));
        Assert.Contains("Called the owner", (await GetDocumentAsync(owner, $"/Provider/Tenants/Details/{id}?tab=notes")).Body!.TextContent);

        // Resend the owner's invitation (no password yet): a new set-your-password email.
        var before = _app.Emails.To(ownerEmail).Count();
        var ownerUserId = (await _app.FindUserAsync(ownerEmail)).Id;
        await PostFormAsync(owner, $"/Provider/Tenants/Details/{id}?tab=admins", a => a.Contains("handler=Resend"));
        Assert.Equal(before + 1, _app.Emails.To(ownerEmail).Count());

        // A second admin who signed up: password reset, then transfer of ownership (needs a fresh code).
        var adminEmail = Unique("kofi") + "@detail.test";
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var admin = new AppUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true, TenantId = id, Type = IdentitySeeder.AdminRole };
            Assert.True((await users.CreateAsync(admin, StrongPassword)).Succeeded);
            await using var platform = _app.CreatePlatformDbContext();
            platform.TenantMemberships.Add(TenantMembership.CreateActive(admin.Id, id, MembershipRole.Admin, DateTime.UtcNow));
            await platform.SaveChangesAsync();
        }
        var adminId = (await _app.FindUserAsync(adminEmail)).Id;
        await PostFormAsync(owner, $"/Provider/Tenants/Details/{id}?tab=admins", a => a.Contains("handler=Reset") && a.Contains(adminId));
        Assert.Contains(_app.Emails.To(adminEmail), e => e.Subject.Contains("Reset your"));

        _app.Clock.Advance(TimeSpan.FromMinutes(6)); // the sign-in's two-factor is no longer recent
        var transfer = $"/Provider/Tenants/TransferOwnership/{id}?userId={adminId}";
        var asked = await PostAsync(owner, transfer);
        Assert.Contains("/Provider/Reauthenticate", Location(asked));
        Assert.Equal(transfer, Uri.UnescapeDataString(Location(await PostAsync(owner, Location(asked), ("Code", Totp.Code(key))))));
        Assert.Contains("tab=admins", Location(await PostAsync(owner, transfer)));

        await using (var platform = _app.CreatePlatformDbContext())
        {
            var roles = await platform.TenantMemberships.Where(m => m.TenantId == id).ToDictionaryAsync(m => m.UserId, m => m.Role);
            Assert.Equal((MembershipRole.Owner, MembershipRole.Admin), (roles[adminId], roles[ownerUserId]));

            var actions = await platform.AuditEntries.Where(a => a.TenantId == id).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
            Assert.Equal(new[]
            {
                "organization.created", "organization.updated", "organization.suspended", "organization.reactivated", "organization.note-added",
                "organization.invitation-resent", "organization.password-reset-sent", "organization.ownership-transferred",
            }, actions);
        }
    }

    [Fact]
    public async Task Organization_List_Searches_Filters_And_Pages()
    {
        var (owner, _) = await OwnerAsync();
        var marker = Unique("zzlist");

        // 27 organizations sharing a marker in their name, to page through.
        await using (var app = _app.CreateDbContext())
        {
            for (var i = 0; i < 27; i++) app.Tenants.Add(new InpremClockingApp.Models.Tenant { Name = $"{marker} {i:00}", TimeZoneId = "UTC", CreatedAt = DateTime.UtcNow });
            await app.SaveChangesAsync();
        }

        var first = await GetDocumentAsync(owner, $"/Provider/Tenants?Search={marker}");
        Assert.Equal(25, first.QuerySelectorAll("[data-testid=organizations] tbody tr").Length);
        Assert.Contains("27 organization(s)", first.QuerySelector("[data-testid=pager]")!.TextContent);
        var second = await GetDocumentAsync(owner, $"/Provider/Tenants?Search={marker}&p=2");
        Assert.Equal(2, second.QuerySelectorAll("[data-testid=organizations] tbody tr").Length);

        // By owner email, and by status.
        var alphaOwner = _app.Data.Alpha.Owner!.Email;
        Assert.Contains(_app.Data.Alpha.Name, (await GetDocumentAsync(owner, $"/Provider/Tenants?Search={Uri.EscapeDataString(alphaOwner)}")).QuerySelector("[data-testid=organizations]")!.TextContent);
        Assert.Contains("No organizations match", (await GetDocumentAsync(owner, $"/Provider/Tenants?Search={marker}&Status=Suspended")).QuerySelector("[data-testid=organizations]")!.TextContent);
    }
}
