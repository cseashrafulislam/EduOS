using EduOS.App.Extensions;
using EduOS.App.Filters;
using EduOS.App.Middleware;
using EduOS.Core.Configurations;
using EduOS.Persistence.Extensions;
using EduOS.Persistence.Seed;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// 1. CONFIGURATION
// =============================================================================
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

// =============================================================================
// 2. CORE INFRASTRUCTURE
// =============================================================================
builder.Services.AddControllersWithViews(options =>
{
    // Add global filters here if needed
})
.AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
//builder.Services.AddMemoryCache(options =>
//{
//    options.SizeLimit = 100_000;
//});

// =============================================================================
// 3. PERSISTENCE LAYER (DbContext + Repositories)
// =============================================================================
builder.Services.AddPersistenceServices(builder.Configuration);

// =============================================================================
// 4. IDENTITY (Auth)
// =============================================================================
builder.Services.AddIdentityConfiguration();

// =============================================================================
// 5. APPLICATION SERVICES (Service layer)
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
// 8. HANGFIRE (Background Jobs)
// =============================================================================
builder.Services.AddHangfireConfiguration(builder.Configuration);

// =============================================================================
// 9. HEALTH CHECKS
// =============================================================================
builder.Services.AddHealthChecks();

// =============================================================================
// BUILD APP
// =============================================================================
var app = builder.Build();

// =============================================================================
// 10. DATABASE INITIALIZATION (Migrations + Seeders)
// =============================================================================
await DatabaseInitializer.InitializeAsync(app.Services, applyMigrations: true);

// =============================================================================
// 11. MIDDLEWARE PIPELINE (ORDER MATTERS!)
// =============================================================================

// Dev-only error page
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // HSTS in production
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Security headers - very early in pipeline
app.UseSecurityHeaders();

// Custom global exception handler
app.UseCustomExceptionMiddleware();

// Status code pages (404, 500, etc.)
app.UseStatusCodePagesWithReExecute("/Error/{0}");

// Static files (wwwroot)
app.UseStaticFiles();

// Routing
app.UseRouting();

// CORS - must be after UseRouting and before UseAuthentication
app.UseCors(CorsExtensions.DefaultPolicy);

// Rate limiting
app.UseRateLimiter();

// Authentication MUST come before Authorization
app.UseAuthentication();
app.UseAuthorization();

// Tenant context - resolves tenant ID for authenticated users
app.UseTenantContext();

// Onboarding guard - redirects incomplete tenants to wizard
app.UseOnboardingGuard();

// =============================================================================
// 12. HANGFIRE DASHBOARD (SuperAdmin only)
// =============================================================================
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "EduOS Background Jobs",
    DisplayStorageConnectionString = false
});

// =============================================================================
// 13. ENDPOINTS
// =============================================================================
app.MapHealthChecks("/health");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapRazorPages();

// =============================================================================
// 14. RECURRING JOBS (Hangfire)
// =============================================================================
// Uncomment when you have these jobs ready:
//
// RecurringJob.AddOrUpdate<ISubscriptionExpiryJob>(
//     "subscription-expiry-check",
//     job => job.RunAsync(),
//     Cron.Daily(2)); // Run at 2 AM daily
//
// RecurringJob.AddOrUpdate<IRenewalReminderJob>(
//     "subscription-renewal-reminder",
//     job => job.RunAsync(),
//     Cron.Daily(9)); // Run at 9 AM daily

app.Run();
