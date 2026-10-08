using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EduOS.Persistence.Seed
{
    /// <summary>
    /// Single entry point to apply migrations and run all seeders in correct order.
    /// Called from Program.cs at startup.
    /// </summary>
    public static class DatabaseInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider, bool applyMigrations = true)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            var logger = services.GetRequiredService<ILogger<EduOSDbContext>>();

            try
            {
                var context = services.GetRequiredService<EduOSDbContext>();
                var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
                var configuration = services.GetRequiredService<IConfiguration>();

                // 1) Apply pending migrations
                if (applyMigrations)
                {
                    // A missing migration assembly otherwise looks like a successful no-op
                    // until the seeders fail with an unrelated "invalid object name" error.
                    var migrationIds = context.Database.GetMigrations();
                    if (!migrationIds.Any())
                        throw new InvalidOperationException(
                            "No EF Core migrations are included in EduOS.Persistence. Generate and validate the " +
                            "initial migration against the final domain model before database initialization.");

                    logger.LogInformation("Applying database migrations...");
                    await context.Database.MigrateAsync();
                    logger.LogInformation("Migrations applied");
                }

                // 2) Seed roles (must run before users)
                logger.LogInformation("Seeding roles...");
                await RoleSeeder.SeedAsync(roleManager);

                // 3) Seed SuperAdmin (depends on roles)
                logger.LogInformation("Seeding SuperAdmin...");
                await SuperAdminSeeder.SeedAsync(context, userManager, roleManager, configuration, logger);

                // 4) Seed subscription plans, features, plan-features (Phase A)
                logger.LogInformation("Seeding subscription plans...");
                await SubscriptionSeeder.SeedAsync(context);

                // 5) Seed data-driven institution presets and product modules
                logger.LogInformation("Seeding platform catalog...");
                await PlatformCatalogSeeder.SeedAsync(context);

                logger.LogInformation("Database initialization completed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database initialization failed");
                throw;
            }
        }
    }
}
