using System.Globalization;
using System.Threading.RateLimiting;
using M365AcademyCrm.Authentication;
using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// UK business: £ amounts and d/m/y dates everywhere, regardless of server locale.
CultureInfo.DefaultThreadCurrentCulture = Ui.UkCulture;
CultureInfo.DefaultThreadCurrentUICulture = Ui.UkCulture;

builder.Services.AddControllersWithViews(options =>
{
    // Every POST form in the CRM is protected; the one non-browser endpoint
    // (POST /api/leads) opts out explicitly and uses an API key instead.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.Configure<CrmOptions>(builder.Configuration.GetSection(CrmOptions.SectionName));
builder.Services.Configure<CompanyOptions>(builder.Configuration.GetSection(CompanyOptions.SectionName));

var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var crmConnectionString = builder.Configuration.GetConnectionString("CrmDb");
var useSqlite = string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase);
if (useSqlite && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Database:Provider=Sqlite is for local development only. Use SqlServer outside Development.");
}

builder.Services.AddDbContext<CrmDbContext>(options =>
{
    if (useSqlite)
    {
        options.UseSqlite(crmConnectionString);
    }
    else
    {
        options.UseSqlServer(crmConnectionString);
    }
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IGraphGroupChecker, GraphGroupChecker>();
builder.Services.AddHttpClient<IMailSender, GraphMailSender>();
builder.Services.AddScoped<SalesUserStore>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<Lookups>();
builder.Services.AddScoped<LeadIntakeService>();
builder.Services.AddScoped<ReportBuilder>();

// Without this, every app-pool recycle or redeploy generates a new Data
// Protection key, silently invalidating every session and antiforgery token.
// CommonApplicationData resolves to C:\ProgramData on IIS/Windows.
var dataProtectionKeyPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "M365AcademyCrm", "dataprotection-keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .SetApplicationName("M365AcademyCrm");

var useDevAuth = builder.Configuration.GetValue<bool>("Crm:DevAuth");
if (useDevAuth && builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(DevAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, _ => { });
}
else
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

    builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        // Chained, not replaced: AddMicrosoftIdentityWebApp's own
        // OnTokenValidated handler does issuer validation.
        var existingOnTokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            if (existingOnTokenValidated is not null)
            {
                await existingOnTokenValidated(context);
            }

            await CrmOpenIdConnectEvents.OnTokenValidatedAsync(context);
        };
    });

    builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.AccessDeniedPath = "/account/access-denied";
        // A sales tool left open all day shouldn't bounce people to sign in mid-task.
        options.ExpireTimeSpan = TimeSpan.FromHours(10);
        options.SlidingExpiration = true;
    });
}

builder.Services.AddAuthorization(options =>
{
    // Everything requires a CRM role unless it opts out with [AllowAnonymous]
    // (the lead intake API, health check, error and access-denied pages).
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(CrmRoles.User, CrmRoles.Admin)
        .Build();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Lead intake: called server-to-server by the website, which already
    // rate-limits its own contact form - this is just a backstop.
    options.AddPolicy("lead-intake", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

if (useDevAuth && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning("Crm:DevAuth is set but ignored outside the Development environment - using Entra ID sign-in.");
}

// Database setup - guarded like the website's LMS migration: left at the
// CHANGE_ME placeholder it logs a warning instead of crashing at startup.
if (string.IsNullOrWhiteSpace(crmConnectionString) || crmConnectionString == "CHANGE_ME")
{
    app.Logger.LogWarning("ConnectionStrings:CrmDb is not configured - skipping CRM database migration. The CRM will not work until it is set.");
}
else
{
    using var migrationScope = app.Services.CreateScope();
    var db = migrationScope.ServiceProvider.GetRequiredService<CrmDbContext>();
    if (useSqlite)
    {
        // Migrations are generated for SQL Server; the throwaway local
        // SQLite database is just created straight from the model.
        db.Database.EnsureCreated();
    }
    else
    {
        db.Database.Migrate();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Run();
