using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Hosting;
using Inprem.Platform.Infrastructure;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// `provider <command>` arguments (server commands, see below) are not host configuration.
var builder = WebApplication.CreateBuilder(ProviderCommands.HostArgs(args));
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// var connection = "Server=.\SQLEXPRESS;Initial Catalog=DB_A65635_inpremdb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
// var connection = "Server=VMI1066750\\SQLEXPRESS;initial catalog=InpemTestDb;";
// var connection = "Data Source=SQL8001.site4now.net;Initial Catalog=db_a93a94_inpremdb;User Id=db_a93a94_inpremdb_admin;Password=REDACTED;";
// Add services to the container.
builder.Services.AddScoped<StaffService>();
builder.Services.AddScoped<VolunteerService>();
builder.Services.AddScoped<StaffClockingService>();
builder.Services.AddScoped<VolunteerClockingService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<TenantAdminService>();
builder.Services.AddScoped<BillingService>();
// BillingBackgroundService is no longer registered (C2-2, step 0.8): background work runs as
// Hangfire jobs on the App site (AddJobs below). Its billing sweep is replaced in Phase 2.

// Multi-tenancy (see multi-tenancy.md): resolves the signed-in user's tenant from a claim on
// their auth cookie, and is the sole source ApplicationDbContext's query filters read from.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<ITenantClock, TenantClock>();
builder.Services.AddScoped<ICurrentTenantProfile, CurrentTenantProfile>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Multi-tenancy platform (docs/multitenancy): PlatformDbContext (schema "platform") on the same
// database. Data-protection keys now live there (see SiteHosting), not in App_Data.
builder.Services.AddPlatformInfrastructure(connectionString!);

builder.Services.AddDefaultIdentity<AppUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Which site this deployment serves (Site:SiteMode = App | Admin | All), host restrictions,
// per-site cookie and data-protection settings. After AddDefaultIdentity so its cookie settings win.
builder.AddSiteHosting();

// Tenant context and memberships (platform.TenantMemberships): the organization a request works in
// comes from the signed-in membership, re-checked on every request by TenantContextMiddleware.
builder.AddTenancy();

// Account security: lockout (C3-7) and the platform's email sender.
builder.AddAccountSecurity();

// Provider users, bootstrap and recovery (design section 5): Provider area access gate,
// shared use cases for the server commands and the setup page, owners health check.
builder.AddProviderSecurity();

// Background jobs (D6): Hangfire storage everywhere; job server only on the App site with
// Jobs:RunServer = true; read-only dashboard for Provider Owners on the Admin site.
builder.AddJobs();

// Registered after AddDefaultIdentity so it overrides Identity's default factory - stamps the
// TenantId claim onto every sign-in (see AppUserClaimsPrincipalFactory, multi-tenancy.md).
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppUserClaimsPrincipalFactory>();

// Replaces Identity's default IUserValidator<AppUser> (C2-10): an email belongs to one account
// platform-wide; one person, any number of memberships (see PlatformUserValidator). RemoveAll first
// since AddDefaultIdentity above already registered the default one, and UserManager runs every
// registered IUserValidator<T>, not just the last one added.
builder.Services.RemoveAll<IUserValidator<AppUser>>();
builder.Services.AddScoped<IUserValidator<AppUser>, PlatformUserValidator>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));

    // Platform-operator only (multi-tenancy.md Phase 4) - tenant onboarding, not reachable by
    // an ordinary tenant Admin no matter how the role is combined.
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole(IdentitySeeder.SuperAdminRole));
});

builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        options.Conventions
            .ConfigureFilter(new IgnoreAntiforgeryTokenAttribute());

        // Centralized admin-only gate for the back office. Keeping this list here (rather than
        // an [Authorize] attribute scattered per-page) makes it easy to see, at a glance, exactly
        // which pages require the Admin role, and hard to accidentally leave a new one unprotected.
        var adminOnlyPages = new[]
        {
            "/BackOffice",
            "/Staff",
            "/StaffDetails",
            "/Volunteer",
            "/VolunteerDetails",
            "/User",
            "/StaffClocking",
            "/VolunteerClocking",
            "/StaffReport",
            "/StaffClockingReport",
            "/VolunteerReport",
            "/VolunteerClockingReport",
            "/HoursWorked",
        };
        foreach (var page in adminOnlyPages)
        {
            options.Conventions.AuthorizePage(page, "AdminOnly");
        }

        // Public self-registration is closed; only an existing Admin can create new accounts.
        options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", "AdminOnly");

        // Tenant onboarding/management is platform-operator only, deliberately separate from
        // AdminOnly - an ordinary tenant Admin must never reach any /Platform/* page. Gated at
        // the folder level (not per-page) so a future page added under /Platform/ is covered
        // automatically - see ROLES.md's "Known gaps" history for why that matters: the API
        // controllers were once left wide open exactly because each one needed its own explicit
        // attribute. See multi-tenancy.md Phase 4 and Part 2, Phase 7.
        options.Conventions.AuthorizeFolder("/Platform", "SuperAdminOnly");
    })
    .AddMvcOptions(option => option.EnableEndpointRouting = false);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
}

// Server commands (`dotnet InpremClockingApp.dll provider bootstrap|recover ...`): run, then exit
// without starting the web server.
if (ProviderCommands.IsCommand(args))
{
    return await ProviderCommands.RunAsync(app.Services, args);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseTenantContext();
app.UseAuthorization();

//app.MapRazorPages();
app.UseEndpoints(endpoints =>
{
    endpoints.MapRazorPages();
    endpoints.MapControllers();
    endpoints.MapProviderHealth();
    endpoints.MapJobsDashboard();
});


app.Run();
return 0;
