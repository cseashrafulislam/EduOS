using EduOS.Core.Entities.Auth;
using Microsoft.AspNetCore.Identity;

namespace EduOS.Persistence.Seed
{
    public static class RoleSeeder
    {
        public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
        {
            string[] roles = { "SuperAdmin", "TenantAdmin", "Admin", "Teacher", "Student", "Staff" };

            foreach (var roleName in roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new ApplicationRole
                    {
                        Name = roleName,
                        NormalizedName = roleName.ToUpper(),
                        Description = $"{roleName} role"
                    });
                }
            }
        }
    }
}