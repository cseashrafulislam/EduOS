using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.Persistence.Seed;

/// <summary>
/// Opt-in, isolated *demonstration database* bootstrap. Never runs against a
/// production database; Identity creates real, salted password hashes.
/// The public login picker deliberately excludes the platform SuperAdmin role.
/// </summary>
public static class DemoInstitutionSeeder
{
    public const string TenantCode = "EDUOS-DEMO";

    public static readonly IReadOnlyList<string> Roles =
    [
        "TenantAdmin", "Admin", "AdmissionOfficer", "Principal", "VicePrincipal",
        "Teacher", "Student", "Parent", "Accountant", "HR", "Librarian",
        "ExamController", "Staff"
    ];

    public static string EmailFor(string role) =>
        $"demo.{role.ToLowerInvariant()}@eduos.example.invalid";

    public static async Task SeedAsync(IServiceProvider provider, string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 16)
            throw new InvalidOperationException(
                "DemoData:SharedPassword must be a unique complex password of at least 16 characters.");

        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<EduOSDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        // Checking the real connection target and existing tenant inventory stops
        // accidental activation on a production/shared database.
        var databaseName = db.Database.GetDbConnection().Database;
        if (!databaseName.StartsWith("EduOS_Demo_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "DemoData is permitted only in a separate SQL Server database named EduOS_Demo_*.");

        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(x =>
                !x.IsDeleted && x.Code != TenantCode && x.Code != "EDUOS-SYSTEM"))
            throw new InvalidOperationException(
                "DemoData refused: the target database contains another institution.");

        await RoleSeeder.SeedAsync(roleManager);
        await SubscriptionSeeder.SeedAsync(db);
        await PlatformCatalogSeeder.SeedAsync(db);
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Code == TenantCode);
        if (tenant == null)
        {
            tenant = new Tenant
            {
                Code = TenantCode,
                Name = "EduOS Demonstration Institute",
                Email = "demo-institute@eduos.example.invalid",
                Address = "Dhaka, Bangladesh (fictional demonstration institute)",
                State = TenantState.Active,
                OnboardingStage = OnboardingStage.Completed,
                OnboardingCompletedAt = DateTime.UtcNow,
                EmailVerifiedAt = DateTime.UtcNow
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }
        else if (tenant.IsDeleted || tenant.State != TenantState.Active)
        {
            throw new InvalidOperationException("The existing demo tenant is deleted or inactive.");
        }

        // Demo-only non-public zero-price plan; this is NOT a paid subscription,
        // receipt or ledger entry, and exists solely in the isolated demo DB.
        var demoPlan = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.Code == "EDUOS-DEMO");
        if (demoPlan == null)
        {
            demoPlan = new SubscriptionPlan
            {
                Code = "EDUOS-DEMO", Name = "Isolated Demonstration Plan",
                MonthlyPrice = 0, YearlyPrice = 0, IsPublic = false, IsActive = true,
                MaxStudents = 100, MaxEmployees = 50, MaxUsers = 50,
                MaxCampuses = 20, MaxStorageMb = 1024
            };
            db.SubscriptionPlans.Add(demoPlan);
            await db.SaveChangesAsync();
        }
        var enabledFeatures = await db.Features.Where(x => x.IsActive && !x.IsDeleted).ToListAsync();
        var existingFeatures = await db.PlanFeatures.Where(x => x.SubscriptionPlanId == demoPlan.Id)
            .Select(x => x.FeatureId).ToListAsync();
        foreach (var feature in enabledFeatures.Where(x => !existingFeatures.Contains(x.Id)))
            db.PlanFeatures.Add(new PlanFeature
            {
                SubscriptionPlanId = demoPlan.Id, FeatureId = feature.Id, IsEnabled = true
            });
        if (!await db.TenantSubscriptions.IgnoreQueryFilters().AnyAsync(x =>
                x.TenantId == tenant.Id && x.SubscriptionPlanId == demoPlan.Id && !x.IsDeleted))
        {
            db.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = tenant.Id, SubscriptionPlanId = demoPlan.Id,
                BillingCycleCode = "Demo", State = SubscriptionState.Active,
                IsTrial = true, PriceSnapshot = 0,
                StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddYears(1),
                AutoRenew = false
            });
        }
        var modules = await db.ProductModules.Where(x => x.IsActive && !x.IsDeleted).ToListAsync();
        var existingModules = await db.TenantModules.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenant.Id && !x.IsDeleted)
            .Select(x => x.ProductModuleId).ToListAsync();
        foreach (var module in modules.Where(x => !existingModules.Contains(x.Id)))
            db.TenantModules.Add(new TenantModule
            {
                TenantId = tenant.Id, ProductModuleId = module.Id,
                IsEnabled = true, EnabledAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var campus = await db.Campuses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Code == "DEMO-C01");
        if (campus == null)
        {
            campus = new Campus
            {
                TenantId = tenant.Id, Code = "DEMO-C01", Name = "Dhaka Main Campus",
                Address = "Dhaka, Bangladesh", IsHeadOffice = true, IsActive = true
            };
            db.Campuses.Add(campus);
            await db.SaveChangesAsync();
        }

        foreach (var roleName in Roles)
        {
            var email = EmailFor(roleName);
            var role = await roleManager.FindByNameAsync(roleName)
                ?? throw new InvalidOperationException($"Demo role {roleName} is missing.");
            if (role.IsSystemRole || roleName.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Platform system roles cannot be public demo users.");

            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email, Email = email, FullName = $"Demo {roleName}",
                    EmailConfirmed = true, IsActive = true, PreferredLanguage = "en-BD"
                };
                Ensure(await userManager.CreateAsync(user, password), $"Create demo {roleName}");
            }
            else if (!user.EmailConfirmed || user.IsDeleted || !user.IsActive)
            {
                throw new InvalidOperationException($"Existing demo {roleName} account is inactive or unverified.");
            }

            var existingRoles = await userManager.GetRolesAsync(user);
            if (existingRoles.Any(x => !x.Equals(roleName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Demo {roleName} account has an unexpected elevated role.");
            if (!existingRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
                Ensure(await userManager.AddToRoleAsync(user, roleName), $"Assign demo {roleName}");

            if (await db.TenantMemberships.IgnoreQueryFilters().AnyAsync(x =>
                    x.UserId == user.Id && x.TenantId != tenant.Id && !x.IsDeleted))
                throw new InvalidOperationException($"Demo {roleName} account also belongs to another institution.");
            if (!await db.TenantMemberships.IgnoreQueryFilters().AnyAsync(x =>
                    x.UserId == user.Id && x.TenantId == tenant.Id && !x.IsDeleted))
            {
                db.TenantMemberships.Add(new TenantMembership
                {
                    TenantId = tenant.Id, UserId = user.Id, IsOwner = roleName == "TenantAdmin",
                    Status = MembershipStatus.Active, DefaultCampusId = campus.Id,
                    LastSelectedCampusId = campus.Id
                });
            }
            if (!await db.UserCampusAccesses.IgnoreQueryFilters().AnyAsync(x =>
                    x.UserId == user.Id && x.TenantId == tenant.Id && x.CampusId == campus.Id && !x.IsDeleted))
            {
                db.UserCampusAccesses.Add(new UserCampusAccess
                {
                    TenantId = tenant.Id, UserId = user.Id, CampusId = campus.Id,
                    IsDefault = true, IsActive = true
                });
            }
            await db.SaveChangesAsync();
        }
    }

    private static void Ensure(IdentityResult result, string action)
    {
        if (result.Succeeded) return;
        throw new InvalidOperationException(
            $"{action} failed: {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }
}
