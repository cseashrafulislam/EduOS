using EduOS.App.Extensions;
using EduOS.App.Filters;
using EduOS.App.Localization;
using EduOS.App.Middleware;
using EduOS.Core.Configurations;
using EduOS.Persistence.Extensions;
using EduOS.Persistence.Seed;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.OpenApi;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// 1. CONFIGURATION
// =============================================================================
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// Production nodes must share durable data-protection keys. Without this, restarts or
// multi-node deployments can invalidate auth/antiforgery cookies and encrypted payloads.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    throw new InvalidOperationException("DataProtection:KeysPath is required in Production and must point to durable protected storage.");
}

// Production schema changes are an explicit release step. Never mutate a real customer
// database implicitly while the web process is starting.
if (builder.Environment.IsProduction() &&
    (builder.Configuration.GetValue<bool>("DatabaseInitialization:Enabled") ||
     builder.Configuration.GetValue<bool>("DatabaseInitialization:ApplyMigrations")))
{
    throw new InvalidOperationException("Automatic database initialization/migration must be disabled in Production. Apply reviewed migrations before starting the application.");
}

// =============================================================================
// 2. CORE INFRASTRUCTURE
// =============================================================================
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddControllersWithViews(options =>
{
    // Add global filters here if needed
})
.AddViewLocalization()
.AddDataAnnotationsLocalization(options =>
{
    options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource));
})
.AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var supportedCultures = new[] { new CultureInfo("en-BD"), new CultureInfo("bn-BD") };

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("en-BD");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("EduOS");
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    var keyDirectory = new DirectoryInfo(dataProtectionKeysPath);
    if (!keyDirectory.Exists) keyDirectory.Create();
    dataProtection.PersistKeysToFileSystem(keyDirectory);
}

// =============================================================================
// 3. PERSISTENCE LAYER
// =============================================================================
builder.Services.AddPersistenceServices(builder.Configuration);

// =============================================================================
// 4. IDENTITY / AUTH
// =============================================================================
builder.Services.AddIdentityConfiguration(builder.Environment);

// =============================================================================
// 5. APPLICATION SERVICES
// =============================================================================
builder.Services.AddApplicationServices(builder.Configuration);

// =============================================================================
// 6. CORS
// =============================================================================
builder.Services.AddCorsConfiguration(builder.Configuration);

// =============================================================================
// 7. RATE LIMITING
// =============================================================================
builder.Services.AddRateLimiterConfiguration();

// =============================================================================
// 8. HANGFIRE
// =============================================================================
builder.Services.AddHangfireConfiguration(builder.Configuration);

// =============================================================================
// 9. HEALTH CHECKS
// =============================================================================
builder.Services.AddHealthChecks();

// =============================================================================
// 10. SWAGGER
// =============================================================================
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "EduOS API",
        Version = "v1",
        Description = "EduOS Multi-Tenant SaaS Education Management API"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter JWT token only.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});

var app = builder.Build();

// =============================================================================
// 11. DATABASE INITIALIZATION
// =============================================================================
var initializeDatabase = app.Configuration.GetValue<bool>("DatabaseInitialization:Enabled");
if (initializeDatabase)
{
    var applyMigrations = app.Configuration.GetValue<bool>("DatabaseInitialization:ApplyMigrations");
    await DatabaseInitializer.InitializeAsync(app.Services, applyMigrations);
}
else
{
    app.Logger.LogInformation("Automatic database initialization is disabled. Run controlled migrations before deployment.");
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
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EduOS API v1");
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

var staticFileContentTypes = new FileExtensionContentTypeProvider();
staticFileContentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticFileContentTypes,
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path;
        if (path.Equals("/service-worker.js", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        }
    }
});

app.UseRequestLocalization();
app.UseRouting();
app.UseCors(CorsExtensions.DefaultPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseTenantContext();
app.UsePrivilegedMfa();
app.UseOnboardingGuard();
app.UseAuthorization();

// =============================================================================
// 13. HANGFIRE DASHBOARD
// =============================================================================
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "EduOS Background Jobs",
    DisplayStorageConnectionString = false
});

// =============================================================================
// 14. ENDPOINTS
// =============================================================================
app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=Account}/{action=Login}/{id?}");
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
