using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduOS.Persistence.Seed;

public static class SuperAdminSeeder
{
    public static async Task SeedAsync(
        EduOSDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IConfiguration configuration,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(configuration);

        const string roleName = "SuperAdmin";
        const string tenantCode = "EDUOS-SYSTEM";

        var email = configuration["SuperAdmin:Email"]?.Trim();
        var password = configuration["SuperAdmin:Password"];
        var fullName = configuration["SuperAdmin:FullName"]?.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            logger?.LogWarning("SuperAdmin bootstrap skipped because SuperAdmin:Email is not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(fullName)) fullName = "Super Admin";

        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null)
        {
            var roleResult = await roleManager.CreateAsync(new ApplicationRole
            {
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant(),
                Description = "Platform-level Super Administrator",
                IsSystemRole = true,
                IsActive = true,
                TenantId = null
            });
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Failed to create SuperAdmin role: {string.Join(", ", roleResult.Errors.Select(x => x.Description))}");
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Code == tenantCode);
        if (tenant == null)
        {
            tenant = new Tenant
            {
                Name = "EduOS System",
                Code = tenantCode,
                Email = email,
                CountryCode = "BD",
                CurrencyCode = "BDT",
                TimeZoneId = "Asia/Dhaka",
                DefaultLanguage = "en",
                State = TenantState.Active,
                OnboardingStage = OnboardingStage.Completed,
                OnboardingCompletedAt = DateTime.UtcNow,
                EmailVerifiedAt = DateTime.UtcNow
            };
            dbContext.Tenants.Add(tenant);
            await dbContext.SaveChangesAsync();
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("SuperAdmin:Password is required for first-time bootstrap and must come from a secret-managed setting.");

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true,
                IsActive = true,
                PreferredLanguage = "en"
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create SuperAdmin: {string.Join(", ", result.Errors.Select(x => x.Description))}");
        }
        else
        {
            var changed = false;
            if (user.FullName != fullName) { user.FullName = fullName; changed = true; }
            if (!user.EmailConfirmed) { user.EmailConfirmed = true; changed = true; }
            if (!user.IsActive) { user.IsActive = true; changed = true; }
            if (changed)
            {
                user.UpdatedAt = DateTime.UtcNow;
                var result = await userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    throw new InvalidOperationException($"Failed to update SuperAdmin: {string.Join(", ", result.Errors.Select(x => x.Description))}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            var result = await userManager.AddToRoleAsync(user, roleName);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to assign SuperAdmin role: {string.Join(", ", result.Errors.Select(x => x.Description))}");
        }

        logger?.LogInformation("SuperAdmin bootstrap verified for {Email}.", email);
    }
}
