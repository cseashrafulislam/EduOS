using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Tenants;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Seed
{
    public static class SuperAdminSeeder
    {
        public static async Task SeedAsync(
            EduOSDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager)
        {
            const string roleName = "SuperAdmin";
            const string email = "superadmin@eduos.com";
            const string password = "Admin@123";
            const string tenantCode = "EDUOS-SYSTEM";

            // 1) Ensure role exists
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    NormalizedName = roleName.ToUpper(),
                    Description = "System Super Administrator"
                });

                if (!roleResult.Succeeded)
                {
                    throw new Exception(string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                }
            }

            // 2) Ensure tenant exists
            var tenant = await dbContext.Tenants
                .FirstOrDefaultAsync(x => x.Code == tenantCode);

            if (tenant == null)
            {
                tenant = new Tenant
                {
                    Name = "EduOS System",
                    Code = tenantCode,
                    //InstitutionType = "System",
                    //OwnerName = "Super Admin",
                    //Email = email,
                    //Phone = null,
                    //AlternatePhone = null,
                    //Address = "System Default Address",
                    //ContactPersonName = "Super Admin",
                    //ContactPersonDesignation = "System Administrator",
                    //ContactPersonEmail = email,
                    //ShortName = "EduOS",
                    //TimeZone = "Asia/Dhaka",
                    //Currency = "BDT",
                    //Country = "Bangladesh",
                    //Division = null,
                    //District = null,
                    //Thana = null,
                    //PostCode = null,
                    //Subdomain = "system",
                    //CustomDomain = null,
                    //IsCustomDomainVerified = false,
                    //DomainVerificationToken = null,
                    //LogoUrl = null,
                    //FaviconUrl = null,
                    //PrimaryColor = "#0d6efd",
                    //SecondaryColor = "#6c757d",
                    //WebsiteUrl = null,
                    //EIIN = null,
                    //RegistrationNumber = null,
                    //EducationBoard = null,
                    //EstablishedDate = DateTime.UtcNow.Date,
                    //InstitutionCode = "SYS-001",
                    //DatabaseName = null,
                    //SchemaName = null,
                    //StorageKey = null,
                    //IsActive = true,
                    //IsEmailVerified = true,
                    //IsSetupCompleted = true,
                    //TrialStartDate = null,
                    //TrialEndDate = null,
                    //SubscriptionExpireAt = null,
                    //IsSuspended = false,
                    //SuspensionReason = null,
                    //LastLoginAt = null,
                    //LastActivityAt = null,
                    //FailedLoginCount = 0,
                    //IsLocked = false,
                    //Status = "Active",
                    //CurrentOnboardingStep = 999,
                    //SetupCompletedAt = DateTime.UtcNow,
                    //Language = "en",
                    //DateFormat = "dd-MMM-yyyy",
                    //LockedUntil = null,
                    //LastPasswordChangedAt = DateTime.UtcNow
                };

                dbContext.Tenants.Add(tenant);
                await dbContext.SaveChangesAsync();
            }

            // 3) Ensure super admin user exists
            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,// "SuperAdmin",
                    Email = email,
                    FullName = "Super Admin",
                    EmailConfirmed = true,
                    IsActive = true,
                    TenantId = (int)tenant.Id,
                    Address = tenant.Address
                };

                var createResult = await userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    throw new Exception(string.Join(", ", createResult.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                user.UserName = email;
                user.Email = email;
                user.FullName = "Super Admin";
                user.EmailConfirmed = true;
                user.IsActive = true;
                user.TenantId = (int)tenant.Id;
                user.Address = tenant.Address;

                var updateResult = await userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    throw new Exception(string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                }

                // reset password
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await userManager.ResetPasswordAsync(user, token, password);

                if (!resetResult.Succeeded)
                {
                    throw new Exception(string.Join(", ", resetResult.Errors.Select(e => e.Description)));
                }
            }

            // 4) Ensure user has SuperAdmin role
            if (!await userManager.IsInRoleAsync(user, roleName))
            {
                var addRoleResult = await userManager.AddToRoleAsync(user, roleName);
                if (!addRoleResult.Succeeded)
                {
                    throw new Exception(string.Join(", ", addRoleResult.Errors.Select(e => e.Description)));
                }
            }

            // 5) Ensure TenantUser mapping exists
            //var tenantUserExists = await dbContext.Set<TenantUser>()
            //    .AnyAsync(x => x.TenantId == tenant.Id && x.UserId == user.Id);

            //if (!tenantUserExists)
            //{
            //    dbContext.Set<TenantUser>().Add(new TenantUser
            //    {
            //        TenantId = tenant.Id,
            //        UserId = user.Id,
            //        IsOwner = true,
            //        IsActive = true
            //    });

            //    await dbContext.SaveChangesAsync();
            //}
        }
    }
}