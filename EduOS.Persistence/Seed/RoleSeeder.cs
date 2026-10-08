using EduOS.Core.Entities.Auth;
using Microsoft.AspNetCore.Identity;

namespace EduOS.Persistence.Seed;

public static class RoleSeeder
{
    private static readonly string[] DefaultRoles =
    {
        "SuperAdmin", "TenantAdmin", "Admin", "AdmissionOfficer",
        "Principal", "VicePrincipal", "Teacher", "Student", "Parent",
        "Accountant", "HR", "Librarian", "ExamController", "Staff"
    };

    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        ArgumentNullException.ThrowIfNull(roleManager);

        foreach (var roleName in DefaultRoles)
        {
            var existing = await roleManager.FindByNameAsync(roleName);
            if (existing != null)
            {
                // This is a platform role: older databases could contain it without
                // IsSystemRole because the bootstrap role seeder omitted that flag.
                if (roleName == "SuperAdmin" && existing.TenantId == null && !existing.IsSystemRole)
                {
                    existing.IsSystemRole = true;
                    var updated = await roleManager.UpdateAsync(existing);
                    EnsureSuccess(updated, roleName, "update");
                }
                continue;
            }

            var role = new ApplicationRole
            {
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant(),
                TenantId = null,
                IsSystemRole = roleName == "SuperAdmin",
                IsActive = true,
                Description = roleName + " role"
            };
            var created = await roleManager.CreateAsync(role);
            EnsureSuccess(created, roleName, "create");
        }
    }

    private static void EnsureSuccess(IdentityResult result, string roleName, string operation)
    {
        if (result.Succeeded) return;
        throw new InvalidOperationException(
            $"Unable to {operation} role '{roleName}': " +
            string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
