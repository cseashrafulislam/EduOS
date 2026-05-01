using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace EduOS.Service.Services.Auth
{
    public interface ICurrentUserService
    {
        string UserId { get; }
        string? Email { get; }
        string? Username { get; }
        int? TenantId { get; }
        string[] Roles { get; }
        bool IsAuthenticated { get; }
        bool IsSuperAdmin { get; }
        Task<UserSessionInfo> GetCurrentUserSessionAsync();
    }

    public class UserSessionInfo
    {
        public string UserId { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Username { get; set; }
        public int? TenantId { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
        public DateTime LastActivityAt { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }

    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private UserSessionInfo? _cachedUserInfo;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        public string UserId => GetClaimValue(ClaimTypes.NameIdentifier) ?? GetClaimValue("sub") ?? "anonymous";

        public string? Email => GetClaimValue(ClaimTypes.Email);

        public string? Username => GetClaimValue(ClaimTypes.Name) ?? GetClaimValue("username");

        public int? TenantId
        {
            get
            {
                var tenantIdClaim = GetClaimValue("TenantId") ?? GetClaimValue("tenant_id");
                if (int.TryParse(tenantIdClaim, out var tenantId))
                    return tenantId;
                return null;
            }
        }

        public string[] Roles => GetClaimValues(ClaimTypes.Role);

        public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

        public bool IsSuperAdmin => Roles.Contains("SuperAdmin") || Roles.Contains("superadmin");

        public async Task<UserSessionInfo> GetCurrentUserSessionAsync()
        {
            if (_cachedUserInfo != null)
                return _cachedUserInfo;

            var httpContext = _httpContextAccessor.HttpContext;
            var user = httpContext?.User;

            var sessionInfo = new UserSessionInfo
            {
                UserId = UserId,
                Email = Email,
                Username = Username,
                TenantId = TenantId,
                Roles = Roles,
                LastActivityAt = DateTime.UtcNow,
                IpAddress = httpContext?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request?.Headers["User-Agent"].ToString()
            };

            _cachedUserInfo = sessionInfo;

            return await Task.FromResult(sessionInfo);
        }

        private string? GetClaimValue(string claimType)
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(claimType);
            return claim?.Value;
        }

        private string[] GetClaimValues(string claimType)
        {
            var claims = _httpContextAccessor.HttpContext?.User?.FindAll(claimType);
            return claims?.Select(c => c.Value).ToArray() ?? Array.Empty<string>();
        }
    }
}
