using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace EduOS.Service.Helpers
{
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IMemoryCache _cache;

        private bool _resolved;
        private long _userId;
        private long _tenantId;
        private string? _fullName;
        private string? _email;
        private List<string> _roles = new();

        public CurrentUserService(
            IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager,
            IMemoryCache cache)
        {
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
            _cache = cache;
        }

        public bool IsAuthenticated =>
            _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

        public long UserId
        {
            get { Resolve(); return _userId; }
        }

        public long TenantId
        {
            get { Resolve(); return _tenantId; }
        }

        public string? FullName
        {
            get { Resolve(); return _fullName; }
        }

        public string? Email
        {
            get { Resolve(); return _email; }
        }

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

            var ctx = _httpContextAccessor.HttpContext;
            if (ctx?.User?.Identity?.IsAuthenticated != true) return;

            // User ID from claims (now parses as long since Identity uses long Id)
            var idStr = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (long.TryParse(idStr, out var id))
                _userId = id;

            _email = ctx.User.FindFirstValue(ClaimTypes.Email);
            _fullName = ctx.User.FindFirstValue("FullName")
                        ?? ctx.User.Identity.Name;

            _roles = ctx.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            // Tenant ID from claim (set during login) or from cache
            var tenantIdStr = ctx.User.FindFirstValue("TenantId");
            if (long.TryParse(tenantIdStr, out var tid))
            {
                _tenantId = tid;
                return;
            }

            // Fallback: read from cache
            var cacheKey = $"tenant:user:{_userId}";
            if (_cache.TryGetValue<long>(cacheKey, out var cachedTenantId))
            {
                _tenantId = cachedTenantId;
            }
        }
    }
}
