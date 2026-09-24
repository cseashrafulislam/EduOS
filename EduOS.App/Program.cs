using EduOS.App.Extensions;
using EduOS.App.Filters;
using EduOS.App.Health;
using EduOS.App.Localization;
using EduOS.App.Middleware;
using EduOS.Core.Configurations;
using EduOS.Persistence.Extensions;
using EduOS.Persistence.Seed;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// 1. CONFIGURATION
// =============================================================================

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

var dataProtectionKeysPath =
    builder.Configuration["DataProtection:KeysPath"];

if (builder.Environment.IsProduction() &&
    string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    throw new InvalidOperationException(
        "DataProtection:KeysPath is required in Production and must point to durable protected storage.");
}

if (builder.Environment.IsProduction())
{
    var connectionString =
        builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is required in Production.");

    var jwtSecret =
        builder.Configuration["JwtSettings:Secret"];

    if (string.IsNullOrWhiteSpace(jwtSecret) ||
        jwtSecret.Length < 32)
    {
        throw new InvalidOperationException(
            "JwtSettings:Secret must be configured with at least 32 characters in Production.");
    }

    var allowedHosts =
        builder.Configuration["AllowedHosts"];

    if (string.IsNullOrWhiteSpace(allowedHosts) ||
        allowedHosts
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Any(x => x == "*"))
    {
        throw new InvalidOperationException(
            "AllowedHosts must explicitly list trusted production hosts; wildcard hosts are not allowed in Production.");
    }

    var corsOrigins =
        builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ??
        Array.Empty<string>();

    if (corsOrigins.Length == 0 ||
        corsOrigins.Any(origin =>
            string.IsNullOrWhiteSpace(origin) ||
            origin.Contains(
                "localhost",
                StringComparison.OrdinalIgnoreCase) ||
            origin.Contains(
                "127.0.0.1",
                StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(
                origin,
                UriKind.Absolute,
                out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(
            "Cors:AllowedOrigins must contain only explicit HTTPS production origins in Production.");
    }
}

if (builder.Environment.IsProduction() &&
    (builder.Configuration.GetValue<bool>(
         "DatabaseInitialization:Enabled") ||
     builder.Configuration.GetValue<bool>(
         "DatabaseInitialization:ApplyMigrations")))
{
    throw new InvalidOperationException(
        "Automatic database initialization/migration must be disabled in Production. Apply reviewed migrations before starting the application.");
}

// =============================================================================
// 2. CORE INFRASTRUCTURE
// =============================================================================

builder.Services.AddLocalization(
    options => options.ResourcesPath = "Resources");

builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider =
            (_, factory) =>
                factory.Create(typeof(SharedResource));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var supportedCultures = new[]
{
    new CultureInfo("en-BD"),
    new CultureInfo("bn-BD")
};

builder.Services.Configure<RequestLocalizationOptions>(
    options =>
    {
        options.DefaultRequestCulture =
            new RequestCulture("en-BD");

        options.SupportedCultures =
            supportedCultures;

        options.SupportedUICultures =
            supportedCultures;

        options.RequestCultureProviders =
            new IRequestCultureProvider[]
            {
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider()
            };
    });

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

var dataProtection =
    builder.Services
        .AddDataProtection()
        .SetApplicationName("EduOS");

if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    var keyDirectory =
        new DirectoryInfo(dataProtectionKeysPath);

    if (!keyDirectory.Exists)
        keyDirectory.Create();

    dataProtection.PersistKeysToFileSystem(
        keyDirectory);
}

// =============================================================================
// 3. PERSISTENCE LAYER
// =============================================================================

builder.Services.AddPersistenceServices(
    builder.Configuration);

// =============================================================================
// 4. IDENTITY / AUTH
// =============================================================================

builder.Services.AddIdentityConfiguration(
    builder.Environment);

// =============================================================================
// 5. APPLICATION SERVICES
// =============================================================================

builder.Services.AddApplicationServices(
    builder.Configuration);

// =============================================================================
// 6. CORS
// =============================================================================

builder.Services.AddCorsConfiguration(
    builder.Configuration);

// =============================================================================
// 7. RATE LIMITING
// =============================================================================

builder.Services.AddRateLimiterConfiguration();

// =============================================================================
// 8. HANGFIRE
// =============================================================================

builder.Services.AddHangfireConfiguration(
    builder.Configuration);

// =============================================================================
// 9. HEALTH CHECKS
// =============================================================================

builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>(
        "database",
        tags: new[] { "ready" });

// =============================================================================
// 10. SWAGGER
// =============================================================================

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "EduOS API",
            Version = "v1",
            Description =
                "EduOS Multi-Tenant SaaS Education Management API"
        });

    c.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Description = "Enter JWT token only.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

    c.AddSecurityRequirement(
        document =>
            new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference(
                        "Bearer",
                        document),
                    new List<string>()
                }
            });
});

var app = builder.Build();

// =============================================================================
// 11. DATABASE INITIALIZATION
// =============================================================================

var initializeDatabase =
    app.Configuration.GetValue<bool>(
        "DatabaseInitialization:Enabled");

if (initializeDatabase)
{
    var applyMigrations =
        app.Configuration.GetValue<bool>(
            "DatabaseInitialization:ApplyMigrations");

    await DatabaseInitializer.InitializeAsync(
        app.Services,
        applyMigrations);
}
else
{
    app.Logger.LogInformation(
        "Automatic database initialization is disabled. Run controlled migrations before deployment.");
}

// =============================================================================
// 12. MIDDLEWARE PIPELINE
// =============================================================================

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "EduOS API v1");

        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseCustomExceptionMiddleware();
app.UseStatusCodePagesWithReExecute("/Error/{0}");

var staticFileContentTypes =
    new FileExtensionContentTypeProvider();

staticFileContentTypes.Mappings[".webmanifest"] =
    "application/manifest+json";

app.UseStaticFiles(
    new StaticFileOptions
    {
        ContentTypeProvider =
            staticFileContentTypes,

        OnPrepareResponse = context =>
        {
            var path =
                context.Context.Request.Path;

            if (path.Equals(
                    "/service-worker.js",
                    StringComparison.OrdinalIgnoreCase) ||
                path.Equals(
                    "/manifest.webmanifest",
                    StringComparison.OrdinalIgnoreCase))
            {
                context.Context.Response
                    .Headers["Cache-Control"] =
                    "no-cache, no-store, must-revalidate";
            }
        }
    });

app.UseRequestLocalization();
app.UseRouting();
app.UseCors(CorsExtensions.DefaultPolicy);
app.UseRateLimiter();

app.UseAuthentication();
app.UseTenantContext();
app.UseOnboardingGuard();
app.UseAuthorization();

// =============================================================================
// 13. HANGFIRE DASHBOARD
// =============================================================================

app.UseHangfireDashboard(
    "/hangfire",
    new DashboardOptions
    {
        Authorization =
            new[]
            {
                new HangfireAuthorizationFilter()
            },

        DashboardTitle =
            "EduOS Background Jobs",

        DisplayStorageConnectionString =
            false
    });

// =============================================================================
// 14. ENDPOINTS
// =============================================================================

app.MapHealthChecks(
        "/health/live",
        new Microsoft.AspNetCore.Diagnostics
            .HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false
        })
    .AllowAnonymous();

app.MapHealthChecks(
        "/health/ready",
        new Microsoft.AspNetCore.Diagnostics
            .HealthChecks.HealthCheckOptions
        {
            Predicate =
                registration =>
                    registration.Tags.Contains("ready")
        })
    .AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Account}/{action=Login}/{id?}");

app.MapRazorPages();

// =============================================================================
// 15. RECURRING JOBS
// =============================================================================

// RecurringJob.AddOrUpdate<ISubscriptionExpiryJob>(
//     "subscription-expiry-check",
//     job => job.RunAsync(),
//     Cron.Daily(2));
//
// RecurringJob.AddOrUpdate<IRenewalReminderJob>(
//     "subscription-renewal-reminder",
//     job => job.RunAsync(),
//     Cron.Daily(9));

app.Run();