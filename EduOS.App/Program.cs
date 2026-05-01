using EduOS.App.Middleware;
using EduOS.Core.Configurations;
using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

builder.Services.AddDbContext<EduOSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;

    options.SignIn.RequireConfirmedEmail = true;
})
.AddEntityFrameworkStores<EduOSDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseSqlServerStorage(
              builder.Configuration.GetConnectionString("DefaultConnection"),
              new SqlServerStorageOptions
              {
                  CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                  SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                  QueuePollInterval = TimeSpan.FromSeconds(15),
                  UseRecommendedIsolationLevel = true,
                  DisableGlobalLocks = true
              }));

builder.Services.AddHangfireServer();
builder.Services.AddHttpContextAccessor();

builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100000;
});

//builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
//builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
//builder.Services.AddScoped<IEmailJob, EmailJob>();
//builder.Services.AddScoped<IEmailService, EmailService>();
//builder.Services.AddScoped<IInstitutionOnboardingService, InstitutionOnboardingService>();
//builder.Services.AddScoped<IDashboardService, DashboardService>();


builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Error/403";
});
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("LoginPolicy", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});



var app = builder.Build();

var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
var memoryCache = app.Services.GetRequiredService<IMemoryCache>();

//UserContext.Configure(
//    accessor,
//    memoryCache,
//    async userId =>
//    {
//        using var scope = app.Services.CreateScope();
//        var db = scope.ServiceProvider.GetRequiredService<EduOSDbContext>();

//        var tenantUser = await db.TenantUsers
//            .AsNoTracking()
//            .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

//        return tenantUser?.TenantId;
//    });



using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var context = services.GetRequiredService<EduOSDbContext>();
    var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    //await context.Database.MigrateAsync();

   // await RoleSeeder.SeedAsync(roleManager);
   // await SuperAdminSeeder.SeedAsync(userManager, roleManager);

  //  await FeatureSeeder.SeedAsync(context);
   // await SubscriptionPlanSeeder.SeedAsync(context);
   // await PlanFeatureSeeder.SeedAsync(context);
}


app.UseCors("AllowAll");

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();
app.UseCustomExceptionMiddleware();

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire");
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();