using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
OrgClock.Configure(builder.Configuration);

// var connection = "Server=.\SQLEXPRESS;Initial Catalog=DB_A65635_inpremdb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
// var connection = "Server=VMI1066750\\SQLEXPRESS;initial catalog=InpemTestDb;";
// var connection = "Data Source=SQL8001.site4now.net;Initial Catalog=db_a93a94_inpremdb;User Id=db_a93a94_inpremdb_admin;Password=REDACTED;";
// Add services to the container.
builder.Services.AddScoped<StaffService>();
builder.Services.AddScoped<VolunteerService>();
builder.Services.AddScoped<StaffClockingService>();
builder.Services.AddScoped<VolunteerClockingService>();
builder.Services.AddScoped<SettingService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<EmailService>();

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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
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
            "/Settings",
            "/StaffClocking",
            "/VolunteerClocking",
            "/StaffReport",
            "/StaffClockingReport",
            "/OneStaffClockingReport",
            "/VolunteerReport",
            "/VolunteerClockingReport",
            "/OneVolunteerClockingReport",
            "/HoursWorked",
        };
        foreach (var page in adminOnlyPages)
        {
            options.Conventions.AuthorizePage(page, "AdminOnly");
        }

        // Public self-registration is closed; only an existing Admin can create new accounts.
        options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", "AdminOnly");
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
