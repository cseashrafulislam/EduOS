using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace EduOS.Core.Helpers
{
    public static class UserContext
    {
        private static IHttpContextAccessor? _http;
        private static IMemoryCache? _cache;

        // optional DB fallback resolver
        // example: userId => tenantId
        private static Func<int, Task<int?>>? _tenantResolver;

        // optional small local fallback cache if IMemoryCache not configured
        private static readonly ConcurrentDictionary<string, (int TenantId, DateTime ExpireAt)> _localTenantCache = new();

        private static readonly TimeSpan _tenantCacheDuration = TimeSpan.FromMinutes(20);

        public static void Configure(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache? memoryCache = null,
            Func<int, Task<int?>>? tenantResolver = null)
        {
            _http = httpContextAccessor;
            _cache = memoryCache;
            _tenantResolver = tenantResolver;
        }

        // ===============================
        // HTTP CONTEXT
        // ===============================
        public static HttpContext? GetHttpContext()
            => _http?.HttpContext;

        public static bool IsAuthenticated()
            => GetHttpContext()?.User?.Identity?.IsAuthenticated ?? false;

        // ===============================
        // USER ID
        // ===============================
        public static string? ResolveUserId()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null)
                return null;

            // 1) Header first (mobile/api)
            if (httpContext.Request.Headers.TryGetValue("X-UserId", out var headerUserId))
            {
                var value = headerUserId.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            var user = httpContext.User;

            // 2) Claims next (cookie/jwt)
            if (user?.Identity != null && user.Identity.IsAuthenticated)
            {
                var claimUserId =
                    user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                    user.FindFirst("sub")?.Value ??
                    user.FindFirst("uid")?.Value ??
                    user.FindFirst("user_id")?.Value;

                if (!string.IsNullOrWhiteSpace(claimUserId))
                    return claimUserId;
            }

            return null;
        }

        public static int? ResolveUserIdInt()
        {
            var userId = ResolveUserId();
            return int.TryParse(userId, out var id) ? id : null;
        }

        public static string RequireUserId()
            => ResolveUserId()
               ?? throw new UnauthorizedAccessException("User is not logged in.");

        public static int RequireUserIdInt()
            => ResolveUserIdInt()
               ?? throw new UnauthorizedAccessException("User id is invalid or missing.");

        // ===============================
        // EMAIL / NAME / ROLE
        // ===============================
        public static string? ResolveEmail()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null)
                return null;

            // header fallback
            if (httpContext.Request.Headers.TryGetValue("X-UserEmail", out var headerEmail))
            {
                var value = headerEmail.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return httpContext.User?.FindFirst(ClaimTypes.Email)?.Value
                   ?? httpContext.User?.FindFirst("email")?.Value;
        }

        public static string? ResolveUserName()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null)
                return null;

            return httpContext.User?.FindFirst(ClaimTypes.Name)?.Value
                   ?? httpContext.User?.Identity?.Name;
        }

        public static string? ResolveRole()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null)
                return null;

            return httpContext.User?.FindFirst(ClaimTypes.Role)?.Value
                   ?? httpContext.User?.FindFirst("role")?.Value;
        }

        // ===============================
        // TENANT ID
        // Resolve order:
        // 1) Header
        // 2) Claim
        // 3) Cache by userId
        // 4) DB fallback via configured resolver
        // ===============================
        public static string? ResolveTenantId()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null)
                return null;

            // 1) Header first (mobile/api)
            if (httpContext.Request.Headers.TryGetValue("X-TenantId", out var headerTenantId))
            {
                var value = headerTenantId.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            var user = httpContext.User;

            // 2) Claim next (cookie/jwt)
            if (user?.Identity != null && user.Identity.IsAuthenticated)
            {
                var claimTenantId =
                    user.FindFirst("tenant_id")?.Value ??
                    user.FindFirst("tenantid")?.Value ??
                    user.FindFirst("tid")?.Value;

                if (!string.IsNullOrWhiteSpace(claimTenantId))
                    return claimTenantId;
            }

            return null;
        }

        public static int? ResolveTenantIdInt()
        {
            var tenantId = ResolveTenantId();
            return int.TryParse(tenantId, out var id) ? id : null;
        }

        public static async Task<int?> ResolveTenantIdIntAsync()
        {

            // user লাগবে cache/db fallback এর জন্য
            var userId = ResolveUserIdInt();
            if (!userId.HasValue)
                return null;

            var cacheKey = $"tenant:user:{userId.Value}";

            // IMemoryCache first
            if (_cache != null && _cache.TryGetValue(cacheKey, out int cachedTenantId))
                return cachedTenantId;

            // header/claim hit হলে fastest path
            var directTenantId = ResolveTenantIdInt();
            if (directTenantId.HasValue)
                return directTenantId.Value;


            // local fallback cache
            if (_localTenantCache.TryGetValue(cacheKey, out var localEntry))
            {
                if (localEntry.ExpireAt > DateTime.UtcNow)
                    return localEntry.TenantId;

                _localTenantCache.TryRemove(cacheKey, out _);
            }

            // DB fallback resolver
            if (_tenantResolver == null)
                return null;

            var resolvedTenantId = await _tenantResolver(userId.Value);
            if (!resolvedTenantId.HasValue)
                return null;

            SetTenantCache(userId.Value, resolvedTenantId.Value);

            return resolvedTenantId.Value;
        }

        public static int RequireTenantIdInt()
            => ResolveTenantIdInt()
               ?? throw new UnauthorizedAccessException("Tenant id not found.");

        public static async Task<int> RequireTenantIdIntAsync()
            => await ResolveTenantIdIntAsync()
               ?? throw new UnauthorizedAccessException("Tenant id not found.");

        // ===============================
        // CACHE HELPERS
        // ===============================
        public static void SetTenantCache(int userId, int tenantId)
        {
            var cacheKey = $"tenant:user:{userId}";

            if (_cache != null)
            {
                _cache.Set(cacheKey, tenantId, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _tenantCacheDuration,
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    Size = 1
                });
            }

            _localTenantCache[cacheKey] = (tenantId, DateTime.UtcNow.Add(_tenantCacheDuration));
        }

        public static void RemoveTenantCache(int userId)
        {
            var cacheKey = $"tenant:user:{userId}";

            _cache?.Remove(cacheKey);
            _localTenantCache.TryRemove(cacheKey, out _);
        }

        public static void ClearAllLocalTenantCache()
        {
            _localTenantCache.Clear();
        }
    }
}