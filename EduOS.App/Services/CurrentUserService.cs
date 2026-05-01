using EduOS.Core.Common;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace EduOS.App.Services
{
    /// <summary>
    /// Provides current user information from HttpContext.
    /// Reads from JWT claims set during authentication.
    /// </summary>
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private int? _impersonatedTenantId;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

        public int UserId
        {
            get
            {
                var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                 ?? User?.FindFirst("sub")?.Value
                                 ?? User?.FindFirst("userId")?.Value;

                return int.TryParse(userIdClaim, out var userId) ? userId : 0;
            }
        }

        public int TenantId
        {
            get
            {
                // Allow impersonation (for SuperAdmin to switch tenants)
                if (_impersonatedTenantId.HasValue)
                    return _impersonatedTenantId.Value;

                // Try header first (for tenant switching)
                var headerTenantId = _httpContextAccessor.HttpContext?
                    .Request.Headers["X-Tenant-Id"].FirstOrDefault();

                if (!string.IsNullOrEmpty(headerTenantId)
                    && int.TryParse(headerTenantId, out var headerId))
                {
                    return headerId;
                }

                // Then JWT claim
                var tenantClaim = User?.FindFirst("tenantId")?.Value;
                return int.TryParse(tenantClaim, out var tenantId) ? tenantId : 0;
            }
        }

        public string UserType => User?.FindFirst("userType")?.Value ?? string.Empty;

        public string? Email => User?.FindFirst(ClaimTypes.Email)?.Value
                              ?? User?.FindFirst("email")?.Value;

        public string? FullName => User?.FindFirst(ClaimTypes.Name)?.Value
                                  ?? User?.FindFirst("name")?.Value;

        public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

        public bool IsSuperAdmin => UserType == "SuperAdmin";

        public List<string> Permissions
        {
            get
            {
                if (User == null) return new List<string>();

                return User.FindAll("permission")
                    .Select(c => c.Value)
                    .ToList();
            }
        }

        public List<string> Roles
        {
            get
            {
                if (User == null) return new List<string>();

                return User.FindAll(ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToList();
            }
        }

        public string? IpAddress
        {
            get
            {
                var context = _httpContextAccessor.HttpContext;
                if (context == null) return null;

                // Check X-Forwarded-For header first (for behind proxy/load balancer)
                var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                {
                    var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    if (ips.Length > 0)
                        return ips[0].Trim();
                }

                return context.Connection.RemoteIpAddress?.ToString();
            }
        }

        public string? UserAgent =>
            _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].FirstOrDefault();

        public bool HasPermission(string permission)
        {
            if (IsSuperAdmin) return true; // SuperAdmin has all permissions
            return Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        }

        public bool HasAnyPermission(params string[] permissions)
        {
            if (IsSuperAdmin) return true;
            return permissions.Any(p => Permissions.Contains(p, StringComparer.OrdinalIgnoreCase));
        }

        public bool HasAllPermissions(params string[] permissions)
        {
            if (IsSuperAdmin) return true;
            return permissions.All(p => Permissions.Contains(p, StringComparer.OrdinalIgnoreCase));
        }

        public bool HasRole(string role)
        {
            return Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
        }

        public void SetTenant(int tenantId)
        {
            // Only SuperAdmin can impersonate other tenants
            if (IsSuperAdmin)
            {
                _impersonatedTenantId = tenantId;
            }
        }

        public void ClearImpersonation()
        {
            _impersonatedTenantId = null;
        }
    }
}