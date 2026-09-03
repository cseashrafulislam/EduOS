using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduOS.Persistence.Seed
{
    /// <summary>
    /// Seeds the platform-level SuperAdmin user and the system tenant.
    /// </summary>
    public static class SuperAdminSeeder
    {
        public static async Task SeedAsync(
            EduOSDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager,
            IConfiguration configuration,
            ILogger? logger = null)
        {
            const string roleName = "SuperAdmin";
            const string tenantCode = "EDUOS-SYSTEM";

            // Bootstrap is opt-in. A public repository must never create a known
            // privileged account from fallback credentials.
            var email = configuration["SuperAdmin:Email"]?.Trim();
            var password = configuration["SuperAdmin:Password"];
            var fullName = configuration["SuperAdmin:FullName"]?.Trim();

            if (string.IsNullOrWhiteSpace(email))
            {
                logger?.LogWarning(
                    "SuperAdmin bootstrap skipped. Configure SuperAdmin:Email through a secret-managed deployment setting when initial access is required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(fullName))
                fullName = "Super Admin";

            var user = await userManager.FindByEmailAsync(email);
            if (user == null && string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "SuperAdmin:Password is required only for first-time SuperAdmin creation and must be supplied through a secret store.");
            }

            // 1) Ensure SuperAdmin role exists
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    NormalizedName = roleName.ToUpper(),
                    Description = "System Super Administrator - Platform owner",
                    IsSystemRole = true,
                    IsActive = true,
                    TenantId = null  // System-wide role
                });

                if (!roleResult.Succeeded)
                {
                    var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to create SuperAdmin role: {errors}");
                }

                logger?.LogInformation("SuperAdmin role created");
            }

            // 2) Ensure system tenant exists
            var tenant = await dbContext.Tenants
                .FirstOrDefaultAsync(x => x.Code == tenantCode);

            if (tenant == null)
            {
                tenant = new Tenant
                {
                    Name = "EduOS System",
                    Code = tenantCode,
                    Email = email,
                    OwnerName = fullName,
                    OwnerEmail = email,
                    InstitutionType = "System",
                    Country = "Bangladesh",
                    Currency = "BDT",
                    CurrencySymbol = "৳",
                    TimeZone = "Asia/Dhaka",
                    Language = "en",
                    DateFormat = "dd-MM-yyyy",
                    PrimaryColor = "#1E40AF",
                    SecondaryColor = "#64748B",
                    AccentColor = "#F59E0B",
                    Status = TenantStatus.Active,
                    OnboardingStep = OnboardingStep.Completed,
                    IsOnboardingComplete = true,
                    OnboardingCompletedAt = DateTime.UtcNow,
                    IsEmailVerified = true,
                    EmailVerifiedAt = DateTime.UtcNow,
                    IsActive = true,
                    ActivatedAt = DateTime.UtcNow
                };

                dbContext.Tenants.Add(tenant);
                await dbContext.SaveChangesAsync();
                logger?.LogInformation("System tenant created: {Code}", tenantCode);
            }

            // 3) Ensure super admin user exists
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    EmailConfirmed = true,
                    IsActive = true,
                    TenantId = null,                  // SuperAdmin has NO tenant
                    UserType = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow
                };

                var createResult = await userManager.CreateAsync(user, password!);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to create SuperAdmin user: {errors}");
                }

                logger?.LogInformation("SuperAdmin user created: {Email}", email);
            }
            else
            {
                // Sync key fields if user already exists
                bool needsUpdate = false;

                if (user.FullName != fullName) { user.FullName = fullName; needsUpdate = true; }
                if (!user.EmailConfirmed) { user.EmailConfirmed = true; needsUpdate = true; }
                if (!user.IsActive) { user.IsActive = true; needsUpdate = true; }
                if (user.UserType != "SuperAdmin") { user.UserType = "SuperAdmin"; needsUpdate = true; }
                if (user.TenantId != null) { user.TenantId = null; needsUpdate = true; }

                if (needsUpdate)
                {
                    user.UpdatedAt = DateTime.UtcNow;
                    var updateResult = await userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                        throw new Exception($"Failed to update SuperAdmin user: {errors}");
                    }
                }
            }

            // 4) Ensure user has SuperAdmin role
            if (!await userManager.IsInRoleAsync(user, roleName))
            {
                var addRoleResult = await userManager.AddToRoleAsync(user, roleName);
                if (!addRoleResult.Succeeded)
                {
                    var errors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
                    throw new Exception($"Failed to add SuperAdmin role: {errors}");
                }

                logger?.LogInformation("SuperAdmin role assigned to {Email}", email);
            }
        }
    }
}
