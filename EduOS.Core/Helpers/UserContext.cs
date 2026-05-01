using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace EduOS.Core.Helpers
{
    public static class UserContext
    {
        private static IHttpContextAccessor? _http;
        private static IMemoryCache? _cache;
        private static Func<int, Task<int?>>? _tenantResolver;

        // Local fallback cache
        private static readonly ConcurrentDictionary<string, (int TenantId, DateTime ExpireAt)> _localTenantCache = new();
        private static readonly ConcurrentDictionary<string, (string Value, DateTime ExpireAt)> _localUserInfoCache = new();

        private static readonly TimeSpan _tenantCacheDuration = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan _userInfoCacheDuration = TimeSpan.FromMinutes(10);

        // Configuration options
        private static bool _enableHeaderFallback = true;
        private static bool _enableClaimFallback = true;
        private static bool _enableDetailedLogging = false;

        public static void Configure(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache? memoryCache = null,
            Func<int, Task<int?>>? tenantResolver = null,
            UserContextOptions? options = null)
        {
            _http = httpContextAccessor;
            _cache = memoryCache;
            _tenantResolver = tenantResolver;

            if (options != null)
            {
                _enableHeaderFallback = options.EnableHeaderFallback;
                _enableClaimFallback = options.EnableClaimFallback;
                _enableDetailedLogging = options.EnableDetailedLogging;
            }
        }

        // ===============================
        // HTTP CONTEXT & BASIC INFO
        // ===============================
        public static HttpContext? GetHttpContext() => _http?.HttpContext;

        public static bool IsAuthenticated() => GetHttpContext()?.User?.Identity?.IsAuthenticated ?? false;

        public static string? GetUserAgent() => GetHttpContext()?.Request.Headers["User-Agent"].ToString();

        public static string? GetClientIP()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null) return null;

            // Check for forwarded IP (when behind proxy/load balancer)
            var forwardedIp = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedIp))
                return forwardedIp.Split(',').First().Trim();

            return httpContext.Connection.RemoteIpAddress?.ToString();
        }

        // ===============================
        // USER ID with multiple strategies
        // ===============================
        public static string? ResolveUserId()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null) return null;

            // Strategy 1: Header (for API/mobile clients)
            if (_enableHeaderFallback && httpContext.Request.Headers.TryGetValue("X-UserId", out var headerUserId))
            {
                var value = headerUserId.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    LogDebug($"UserId resolved from header: {value}");
                    return value;
                }
            }

            // Strategy 2: Header alternatives
            if (_enableHeaderFallback)
            {
                var alternativeHeaders = new[] { "X-User-Id", "UserId", "user_id" };
                foreach (var header in alternativeHeaders)
                {
                    if (httpContext.Request.Headers.TryGetValue(header, out var value))
                    {
                        var userId = value.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(userId))
                        {
                            LogDebug($"UserId resolved from header '{header}': {userId}");
                            return userId;
                        }
                    }
                }
            }

            // Strategy 3: Claims (JWT/Cookie)
            if (_enableClaimFallback && httpContext.User?.Identity != null && httpContext.User.Identity.IsAuthenticated)
            {
                var claimUserId =
                    httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                    httpContext.User.FindFirst("sub")?.Value ??
                    httpContext.User.FindFirst("uid")?.Value ??
                    httpContext.User.FindFirst("user_id")?.Value ??
                    httpContext.User.FindFirst("id")?.Value;

                if (!string.IsNullOrWhiteSpace(claimUserId))
                {
                    LogDebug($"UserId resolved from claim: {claimUserId}");
                    return claimUserId;
                }
            }

            // Strategy 4: Query string (for special cases like email links)
            if (httpContext.Request.Query.TryGetValue("userId", out var queryUserId))
            {
                var value = queryUserId.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    LogDebug($"UserId resolved from query string: {value}");
                    return value;
                }
            }

            LogDebug("UserId could not be resolved from any source");
            return null;
        }

        public static int? ResolveUserIdInt()
        {
            var userId = ResolveUserId();
            if (int.TryParse(userId, out var id))
                return id;

            // Try to extract from claims as integer directly
            var httpContext = GetHttpContext();
            var claimValue = httpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(claimValue, out id))
                return id;

            return null;
        }

        public static string RequireUserId() => ResolveUserId() ?? throw new UnauthorizedAccessException("User is not logged in.");

        public static int RequireUserIdInt() => ResolveUserIdInt() ?? throw new UnauthorizedAccessException("User id is invalid or missing.");

        // ===============================
        // EMAIL with caching
        // ===============================
        public static string? ResolveEmail()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null) return null;

            // Check cache first
            var cacheKey = $"user:email:{GetClientIP()}";
            if (_cache != null && _cache.TryGetValue<string>(cacheKey, out var cachedEmail))
                return cachedEmail;

            string? email = null;

            // Strategy 1: Header
            if (_enableHeaderFallback)
            {
                var emailHeaders = new[] { "X-UserEmail", "X-Email", "Email", "email" };
                foreach (var header in emailHeaders)
                {
                    if (httpContext.Request.Headers.TryGetValue(header, out var headerValue))
                    {
                        email = headerValue.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(email))
                            break;
                    }
                }
            }

            // Strategy 2: Claims
            if (string.IsNullOrEmpty(email) && httpContext.User != null)
            {
                email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value ??
                        httpContext.User.FindFirst("email")?.Value;
            }

            // Cache if found
            if (!string.IsNullOrEmpty(email) && _cache != null)
            {
                _cache.Set(cacheKey, email, TimeSpan.FromMinutes(5));
            }

            return email;
        }

        // ===============================
        // USERNAME with validation
        // ===============================
        public static string? ResolveUserName()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null) return null;

            var username = httpContext.User?.FindFirst(ClaimTypes.Name)?.Value ??
                          httpContext.User?.Identity?.Name ??
                          httpContext.User?.FindFirst("username")?.Value;

            // Sanitize username
            if (!string.IsNullOrEmpty(username))
            {
                username = username.Trim();
                // Remove any invalid characters if needed
                username = Regex.Replace(username, @"[^\w\-\.@]", "");
            }

            return username;
        }

        // ===============================
        // ROLES (multiple roles support)
        // ===============================
        public static string? ResolveRole() => ResolveRoles().FirstOrDefault();

        public static string[] ResolveRoles()
        {
            var httpContext = GetHttpContext();
            if (httpContext?.User == null) return Array.Empty<string>();

            var roles = httpContext.User.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .Union(httpContext.User.FindAll("role").Select(c => c.Value))
                .Union(httpContext.User.FindAll("roles").SelectMany(c => c.Value.Split(',')))
                .Distinct()
                .ToArray();

            return roles;
        }

        public static bool IsInRole(string role)
        {
            var roles = ResolveRoles();
            return roles.Contains(role, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsSuperAdmin() => IsInRole("SuperAdmin") || IsInRole("superadmin");

        // ===============================
        // PERMISSIONS (if stored in claims)
        // ===============================
        public static string[] ResolvePermissions()
        {
            var httpContext = GetHttpContext();
            if (httpContext?.User == null) return Array.Empty<string>();

            var permissions = httpContext.User.FindAll("permission")
                .Select(c => c.Value)
                .Union(httpContext.User.FindAll("Permission").Select(c => c.Value))
                .ToArray();

            return permissions;
        }

        public static bool HasPermission(string permission)
        {
            var permissions = ResolvePermissions();
            return permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
        }

        // ===============================
        // TENANT ID - Enhanced version
        // ===============================
        public static string? ResolveTenantId()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null) return null;

            // Strategy 1: Header (highest priority)
            if (_enableHeaderFallback)
            {
                var tenantHeaders = new[] { "X-TenantId", "X-Tenant-Id", "TenantId", "tenant_id", "tid" };
                foreach (var header in tenantHeaders)
                {
                    if (httpContext.Request.Headers.TryGetValue(header, out var headerTenantId))
                    {
                        var value = headerTenantId.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            LogDebug($"TenantId resolved from header '{header}': {value}");
                            return value;
                        }
                    }
                }
            }

            // Strategy 2: Claims
            if (_enableClaimFallback && httpContext.User?.Identity != null && httpContext.User.Identity.IsAuthenticated)
            {
                var claimTenantId =
                    httpContext.User.FindFirst("tenant_id")?.Value ??
                    httpContext.User.FindFirst("tenantid")?.Value ??
                    httpContext.User.FindFirst("tid")?.Value ??
                    httpContext.User.FindFirst("TenantId")?.Value;

                if (!string.IsNullOrWhiteSpace(claimTenantId))
                {
                    LogDebug($"TenantId resolved from claim: {claimTenantId}");
                    return claimTenantId;
                }
            }

            // Strategy 3: Query string (for debugging/testing)
            if (httpContext.Request.Query.TryGetValue("tenantId", out var queryTenantId))
            {
                var value = queryTenantId.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    LogDebug($"TenantId resolved from query string: {value}");
                    return value;
                }
            }

            return null;
        }

        public static int? ResolveTenantIdInt() => ParseTenantId(ResolveTenantId());

        public static async Task<int?> ResolveTenantIdIntAsync()
        {
            var userId = ResolveUserIdInt();
            if (!userId.HasValue) return null;

            var cacheKey = $"tenant:user:{userId.Value}";

            // Check IMemoryCache first
            if (_cache != null && _cache.TryGetValue(cacheKey, out int cachedTenantId))
            {
                LogDebug($"TenantId resolved from memory cache for user {userId}: {cachedTenantId}");
                return cachedTenantId;
            }

            // Check header/claim directly (fastest)
            var directTenantId = ResolveTenantIdInt();
            if (directTenantId.HasValue)
            {
                // Cache for future requests
                SetTenantCache(userId.Value, directTenantId.Value);
                return directTenantId.Value;
            }

            // Check local fallback cache
            if (_localTenantCache.TryGetValue(cacheKey, out var localEntry))
            {
                if (localEntry.ExpireAt > DateTime.UtcNow)
                {
                    LogDebug($"TenantId resolved from local cache for user {userId}: {localEntry.TenantId}");
                    return localEntry.TenantId;
                }
                _localTenantCache.TryRemove(cacheKey, out _);
            }

            // DB fallback resolver
            if (_tenantResolver == null)
            {
                LogDebug($"No tenant resolver configured for user {userId}");
                return null;
            }

            var resolvedTenantId = await _tenantResolver(userId.Value);
            if (!resolvedTenantId.HasValue)
            {
                LogDebug($"No tenant found for user {userId}");
                return null;
            }

            SetTenantCache(userId.Value, resolvedTenantId.Value);
            LogDebug($"TenantId resolved from database for user {userId}: {resolvedTenantId}");

            return resolvedTenantId.Value;
        }

        public static int RequireTenantIdInt() => ResolveTenantIdInt() ?? throw new UnauthorizedAccessException("Tenant id not found.");

        public static async Task<int> RequireTenantIdIntAsync() => await ResolveTenantIdIntAsync() ?? throw new UnauthorizedAccessException("Tenant id not found.");

        // ===============================
        // SESSION / REQUEST INFO
        // ===============================
        public static string? GetRequestId()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null) return null;

            // Try to get from header first
            if (httpContext.Request.Headers.TryGetValue("X-Request-Id", out var requestId))
                return requestId.ToString();

            // Generate if not exists
            var newRequestId = Guid.NewGuid().ToString();
            httpContext.Items["RequestId"] = newRequestId;
            return newRequestId;
        }

        public static string? GetCorrelationId()
        {
            var httpContext = GetHttpContext();
            if (httpContext == null) return null;

            if (httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
                return correlationId.ToString();

            return GetRequestId();
        }

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
                    Size = 1,
                    Priority = CacheItemPriority.Normal
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

        public static void ClearAllLocalTenantCache() => _localTenantCache.Clear();

        public static void ClearAllUserCache()
        {
            _localTenantCache.Clear();
            _localUserInfoCache.Clear();
        }

        // ===============================
        // UTILITY METHODS
        // ===============================
        private static int? ParseTenantId(string? tenantIdString)
        {
            if (string.IsNullOrEmpty(tenantIdString)) return null;

            // Handle "tenant-123" format
            if (tenantIdString.StartsWith("tenant-", StringComparison.OrdinalIgnoreCase))
            {
                var parts = tenantIdString.Split('-');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var id))
                    return id;
            }

            // Direct integer parsing
            if (int.TryParse(tenantIdString, out var tenantId))
                return tenantId;

            return null;
        }

        private static void LogDebug(string message)
        {
            if (_enableDetailedLogging)
            {
                // Use your logger here
                // _logger?.LogDebug(message);
                System.Diagnostics.Debug.WriteLine($"[UserContext] {message}");
            }
        }

        // ===============================
        // VALIDATION METHODS
        // ===============================
        public static bool HasValidTenant()
        {
            var tenantId = ResolveTenantIdInt();
            return tenantId.HasValue && tenantId.Value > 0;
        }

        public static async Task<bool> HasValidTenantAsync()
        {
            var tenantId = await ResolveTenantIdIntAsync();
            return tenantId.HasValue && tenantId.Value > 0;
        }

        public static bool IsTenantContextAvailable() => _tenantResolver != null;

        // ===============================
        // BULK RESOLUTION (for performance)
        // ===============================
        public static async Task<UserContextInfo> ResolveAllAsync()
        {
            var userId = ResolveUserIdInt();
            var tenantId = await ResolveTenantIdIntAsync();

            return new UserContextInfo
            {
                UserId = userId,
                TenantId = tenantId,
                Email = ResolveEmail(),
                Username = ResolveUserName(),
                Roles = ResolveRoles(),
                Permissions = ResolvePermissions(),
                IsAuthenticated = IsAuthenticated(),
                ClientIP = GetClientIP(),
                UserAgent = GetUserAgent(),
                RequestId = GetRequestId(),
                CorrelationId = GetCorrelationId()
            };
        }
    }

    // ===============================
    // OPTIONS CLASS
    // ===============================
    public class UserContextOptions
    {
        public bool EnableHeaderFallback { get; set; } = true;
        public bool EnableClaimFallback { get; set; } = true;
        public bool EnableDetailedLogging { get; set; } = false;
        public TimeSpan TenantCacheDuration { get; set; } = TimeSpan.FromMinutes(20);
        public TimeSpan UserInfoCacheDuration { get; set; } = TimeSpan.FromMinutes(10);
    }

    // ===============================
    // CONTEXT INFO DTO
    // ===============================
    public class UserContextInfo
    {
        public int? UserId { get; set; }
        public int? TenantId { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
        public string[] Permissions { get; set; } = Array.Empty<string>();
        public bool IsAuthenticated { get; set; }
        public string? ClientIP { get; set; }
        public string? UserAgent { get; set; }
        public string? RequestId { get; set; }
        public string? CorrelationId { get; set; }

        public override string ToString()
        {
            return $"User {{ Id: {UserId}, Tenant: {TenantId}, Email: {Email}, Roles: {string.Join(",", Roles)} }}";
        }
    }

    // ===============================
    // EXTENSION METHODS FOR CLAIMS PRINCIPAL
    // ===============================
    public static class ClaimsPrincipalExtensions
    {
        public static int? GetUserId(this ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                             principal.FindFirst("sub")?.Value;
            return int.TryParse(userIdClaim, out var userId) ? userId : null;
        }

        public static int? GetTenantId(this ClaimsPrincipal principal)
        {
            var tenantIdClaim = principal.FindFirst("tenant_id")?.Value ??
                               principal.FindFirst("TenantId")?.Value;
            return int.TryParse(tenantIdClaim, out var tenantId) ? tenantId : null;
        }

        public static string? GetEmail(this ClaimsPrincipal principal)
        {
            return principal.FindFirst(ClaimTypes.Email)?.Value;
        }

        public static bool HasPermission(this ClaimsPrincipal principal, string permission)
        {
            return principal.HasClaim("permission", permission);
        }
    }
}