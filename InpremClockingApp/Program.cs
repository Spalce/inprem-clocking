using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
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

var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddHostedService<BillingBackgroundService>();

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

// Persist Data Protection keys outside the deployed output so auth cookies survive
// app pool recycles and redeployments instead of forcing everyone to log in again.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys")))
    .SetApplicationName("InpremClockingApp");

builder.Services.AddDefaultIdentity<AppUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Registered after AddDefaultIdentity so it overrides Identity's default factory - stamps the
// TenantId claim onto every sign-in (see AppUserClaimsPrincipalFactory, multi-tenancy.md).
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppUserClaimsPrincipalFactory>();

// Replaces Identity's default IUserValidator<AppUser>, whose username-uniqueness check is
// global across every tenant - see TenantAwareUserValidator for why that's wrong for a SaaS
// platform where two different organizations may share an admin's email, and what else (Login,
// ForgotPassword, ResetPassword) had to change alongside it to make that safe. RemoveAll first
// since AddDefaultIdentity above already registered the default one, and UserManager runs every
// registered IUserValidator<T>, not just the last one added.
builder.Services.RemoveAll<IUserValidator<AppUser>>();
builder.Services.AddScoped<IUserValidator<AppUser>, TenantAwareUserValidator>();

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
            "/Volunteer",
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
app.UseAuthorization();

//app.MapRazorPages();
app.UseEndpoints(endpoints =>
{
    endpoints.MapRazorPages();
    endpoints.MapControllers();
});


app.Run();
