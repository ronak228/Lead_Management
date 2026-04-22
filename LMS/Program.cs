using LeadManagementSystem.Data;
using LeadManagementSystem.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Configuration.AddEnvironmentVariables();

// MVC
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add<Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute>());

// File upload
builder.Services.Configure<FormOptions>(x =>
{
    x.MultipartBodyLengthLimit = 2 * 1024 * 1024;
});

// Session - Enhanced for production stability
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    
    // 🔥 CRITICAL FOR HTTP HOSTING:
    options.Cookie.SecurePolicy = CookieSecurePolicy.None;  // Allow HTTP cookies
    options.Cookie.SameSite = SameSiteMode.Lax;             // Relaxed for free hosting
    options.Cookie.Path = "/";                               // Ensure path is correct
    options.Cookie.Domain = null;                            // Let browser handle domain
    
    options.Cookie.Name = ".LeadMgmt.Session";
});

// Database
builder.Services.AddNpgsqlDataSource(
    builder.Configuration.GetConnectionString("DefaultConnection")!
);

// Services
builder.Services.AddScoped<DbHelper>();
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

// Error handler
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        var feature = ctx.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;

        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "text/html; charset=utf-8";

        var msg = app.Environment.IsDevelopment() && ex != null
            ? System.Net.WebUtility.HtmlEncode(ex.ToString())
            : "Something went wrong.";

        await ctx.Response.WriteAsync($"<h2>Error</h2><p>{msg}</p>");
    });
});

if (!app.Environment.IsDevelopment())
{
    // Only use HSTS if HTTPS is available
    var useHttps = app.Configuration.GetValue<bool>("UseHttps", true);
    if (useHttps)
        app.UseHsts();
}

// Only redirect to HTTPS if configured AND available
var forceHttps = app.Configuration.GetValue<bool>("UseHttps", true);
if (forceHttps && !app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

// 🔥 CRITICAL SESSION FIX: Must be before UseAuthorization
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();