using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace EduOS.Service.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static string GetUserId(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirstValue("sub")
                   ?? "unknown";
        }

        public static int? GetTenantId(this ClaimsPrincipal user)
        {
            var tenantIdClaim = user.FindFirstValue("TenantId") ?? user.FindFirstValue("tenant_id");
            if (int.TryParse(tenantIdClaim, out var tenantId))
                return tenantId;
            return null;
        }

        public static string GetEmail(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        }

        public static bool IsSuperAdmin(this ClaimsPrincipal user)
        {
            return user.IsInRole("SuperAdmin") || user.IsInRole("superadmin");
        }

        public static bool HasPermission(this ClaimsPrincipal user, string permission)
        {
            return user.Claims.Any(c => c.Type == "Permissions" && c.Value == permission);
        }
    }
}
