using EduOS.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace EduOS.Service.Helpers
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        private bool _resolved;
        private long _userId;
        private long _tenantId;
        private string? _fullName;
        private string? _email;
        private List<string> _roles = new();

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public bool IsAuthenticated =>
            _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

        public long UserId { get { Resolve(); return _userId; } }
        public long TenantId { get { Resolve(); return _tenantId; } }
        public string? FullName { get { Resolve(); return _fullName; } }
        public string? Email { get { Resolve(); return _email; } }

        public IReadOnlyList<string> Roles
        {
            get { Resolve(); return _roles; }
        }

        public bool IsSuperAdmin =>
            _httpContextAccessor.HttpContext?.User?.IsInRole("SuperAdmin") == true;

        public bool IsTenantAdmin =>
            _httpContextAccessor.HttpContext?.User?.IsInRole("TenantAdmin") == true;

        public bool IsInRole(string role) =>
            _httpContextAccessor.HttpContext?.User?.IsInRole(role) == true;

        public string? IpAddress =>
            _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

        public string? UserAgent =>
            _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].FirstOrDefault();

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var context = _httpContextAccessor.HttpContext;
            var user = context?.User;
            if (user?.Identity?.IsAuthenticated != true) return;

            var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (long.TryParse(idStr, out var id)) _userId = id;

            // TenantContextMiddleware resolves the canonical membership from the current
            // ApplicationUser and only sets this item after validating tenant state.
            // Never fall back to cookie/JWT tenant claims here: those claims can remain
            // stale after an administrator moves a user to another tenant.
            if (context?.Items.TryGetValue("TenantId", out var tenantValue) == true
                && tenantValue is long tenantId
                && tenantId > 0)
            {
                _tenantId = tenantId;
            }

            _email = user.FindFirstValue(ClaimTypes.Email);
            _fullName = user.FindFirstValue("FullName") ?? user.Identity.Name;

            _roles = user.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();
        }
    }
}
