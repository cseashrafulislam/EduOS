using EduOS.App.Extensions;
using EduOS.App.Filters;
using EduOS.App.Middleware;
using EduOS.Core.Configurations;
using EduOS.Persistence.Extensions;
using EduOS.Persistence.Seed;
using Hangfire;
using Microsoft.OpenApi;


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
    opts.JsonSerializerOptions.PropertyNamingPolicy =
        System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

// =============================================================================
// 3. PERSISTENCE LAYER
// =============================================================================
builder.Services.AddPersistenceServices(builder.Configuration);

// =============================================================================
// 4. IDENTITY / AUTH
// =============================================================================
builder.Services.AddIdentityConfiguration();

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
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

// =============================================================================
// BUILD APP
// =============================================================================
var app = builder.Build();

// =============================================================================
// 11. DATABASE INITIALIZATION
// =============================================================================
await DatabaseInitializer.InitializeAsync(app.Services, applyMigrations: true);

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

// HTTPS redirection should be outside environment block
app.UseHttpsRedirection();

// Security headers should be early
app.UseSecurityHeaders();

// Global exception handler
app.UseCustomExceptionMiddleware();

// Status code pages
app.UseStatusCodePagesWithReExecute("/Error/{0}");

// Static files
app.UseStaticFiles();

// Routing
app.UseRouting();

// CORS must be after routing and before auth
app.UseCors(CorsExtensions.DefaultPolicy);

// Rate limiting
app.UseRateLimiter();

// Authentication first
app.UseAuthentication();

// Tenant context should come after authentication
app.UseTenantContext();

// Onboarding guard depends on tenant context
app.UseOnboardingGuard();

// Authorization after tenant/onboarding context
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
app.MapHealthChecks("/health");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

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