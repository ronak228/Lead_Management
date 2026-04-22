using LeadManagementSystem.Data;
using LeadManagementSystem.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Configuration: Support appsettings.json → user-secrets → environment variables
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    // Load user secrets in development (run: dotnet user-secrets set "ConnectionStrings:DefaultConnection" "...")
    builder.Configuration.AddUserSecrets<Program>();
}

// Environment variables override all (for production deployment)
builder.Configuration.AddEnvironmentVariables();

// MVC
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add<Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute>());

// File upload: 2 MB multipart limit
builder.Services.Configure<FormOptions>(x =>
{
    x.MultipartBodyLengthLimit = 2 * 1024 * 1024; // 2 MB
});

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout        = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly    = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite    = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
        : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    options.Cookie.Name        = ".LeadMgmt.Session";
});

// Database: Use NpgsqlDataSource for connection pooling (singleton)
// Maintains ~20 idle connections by default, significantly improves performance
builder.Services.AddNpgsqlDataSource(builder.Configuration.GetConnectionString("DefaultConnection")!);

// DB helper (scoped) — uses singleton NpgsqlDataSource
builder.Services.AddScoped<DbHelper>();

// Service layer (scoped)
builder.Services.AddScoped<ClientService>();

// Email service (scoped) — for notifications
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

// ── Global exception handler ─────────────────────────────────────────────────
// Uses an inline delegate so there is no dependency on a missing HomeController.
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        var feature = ctx.Features.Get<IExceptionHandlerFeature>();
        var ex      = feature?.Error;

        ctx.Response.StatusCode  = 500;
        ctx.Response.ContentType = "text/html; charset=utf-8";

        var msg = app.Environment.IsDevelopment() && ex != null
            ? System.Net.WebUtility.HtmlEncode(ex.ToString())
            : "Something went wrong. Please try again or contact your administrator.";

        // Show a friendly error page that matches the site look
        await ctx.Response.WriteAsync($@"<!DOCTYPE html>
<html lang='en'>
<head><meta charset='utf-8'/>
<meta name='viewport' content='width=device-width,initial-scale=1'/>
<title>Error – Lead Management</title>
<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css' rel='stylesheet'/>
<link href='https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css' rel='stylesheet'/>
</head>
<body class='bg-light d-flex align-items-center justify-content-center' style='min-height:100vh;'>
  <div class='text-center p-5'>
    <i class='bi bi-exclamation-triangle-fill text-danger' style='font-size:3rem;'></i>
    <h4 class='mt-3'>An error occurred</h4>
    <p class='text-muted'>{msg}</p>
    <a href='/' class='btn btn-primary mt-2'>Go to Dashboard</a>
  </div>
</body></html>");
    });
});

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
