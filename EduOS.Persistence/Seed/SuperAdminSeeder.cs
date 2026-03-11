using EduOS.Core.Entities.Auth;
using Microsoft.AspNetCore.Identity;

namespace EduOS.Persistence.Seed
{
    public static class SuperAdminSeeder
    {
        public static async Task SeedAsync(
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager)
        {
            const string roleName = "SuperAdmin";
            const string email = "superadmin@eduos.com";
            const string password = "Admin@123";

            // ensure role exists
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = roleName,
                    NormalizedName = roleName.ToUpper(),
                    Description = "System Super Administrator"
                });
            }

            // check existing user
            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                var superAdmin = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = "Super Admin",
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(superAdmin, password);

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(superAdmin, roleName);
                }
            }
            else
            {
                // ensure role assigned
                if (!await userManager.IsInRoleAsync(user, roleName))
                {
                    await userManager.AddToRoleAsync(user, roleName);
                }
            }
        }
    }
}