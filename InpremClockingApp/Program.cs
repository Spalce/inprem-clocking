// BoldReports removed: no BoldReports license or report designer registration
using InpremClockingApp.Data;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// Syncfusion removed: no Syncfusion licensing or scripts required
using System;

var builder = WebApplication.CreateBuilder(args);
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

builder.Services.AddDefaultIdentity<AppUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        options.Conventions
            .ConfigureFilter(new IgnoreAntiforgeryTokenAttribute());
    })
    .AddMvcOptions(option => option.EnableEndpointRouting = false);

var app = builder.Build();
// BoldReports and Syncfusion removed: no license registration or report designer registration
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
