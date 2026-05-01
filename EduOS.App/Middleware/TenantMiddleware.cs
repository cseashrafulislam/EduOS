using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EduOS.App.Middleware
{
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantMiddleware> _logger;
        private readonly IMemoryCache _cache;

        // Tenant resolution strategies in priority order
        private readonly TenantResolutionStrategy[] _strategies = new[]
        {
            TenantResolutionStrategy.Header,
            TenantResolutionStrategy.Subdomain,
            TenantResolutionStrategy.JwtClaim,
            TenantResolutionStrategy.QueryString
        };

        public TenantMiddleware(
            RequestDelegate next,
            ILogger<TenantMiddleware> logger,
            IMemoryCache cache)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantService tenantService,
            ICurrentUserService currentUser)
        {
            TenantInfo? tenant = null;
            string? tenantIdentifier = null;
            TenantResolutionStrategy? usedStrategy = null;

            try
            {
                // Try each strategy to resolve tenant
                foreach (var strategy in _strategies)
                {
                    tenantIdentifier = await ResolveTenantIdentifierAsync(context, strategy);

                    if (!string.IsNullOrEmpty(tenantIdentifier))
                    {
                        usedStrategy = strategy;
                        tenant = await GetOrResolveTenantAsync(tenantService, tenantIdentifier);

                        if (tenant != null)
                            break;
                    }
                }

                // Handle tenant resolution
                if (tenant == null)
                {
                    await HandleTenantNotFoundAsync(context, tenantIdentifier, usedStrategy);
                    return;
                }

                // Validate tenant status
                if (!await ValidateTenantAccessAsync(tenant, context))
                {
                    await HandleTenantAccessDeniedAsync(context, tenant);
                    return;
                }

                // Set tenant context
                await SetTenantContextAsync(context, currentUser, tenant);

                // Add tenant headers for downstream services
                AddTenantHeaders(context, tenant);

                _logger.LogDebug("Tenant resolved: {TenantId} ({TenantName}) using {Strategy} strategy",
                    tenant.Id, tenant.Name, usedStrategy);

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in tenant middleware for identifier: {Identifier}", tenantIdentifier);
                await HandleTenantResolutionErrorAsync(context, ex);
            }
        }

        #region Tenant Resolution Strategies

        private async Task<string?> ResolveTenantIdentifierAsync(HttpContext context, TenantResolutionStrategy strategy)
        {
            return strategy switch
            {
                TenantResolutionStrategy.Header => await FromHeaderAsync(context),
                TenantResolutionStrategy.Subdomain => await FromSubdomainAsync(context),
                TenantResolutionStrategy.JwtClaim => await FromJwtClaimAsync(context),
                TenantResolutionStrategy.QueryString => await FromQueryStringAsync(context),
                TenantResolutionStrategy.Path => await FromPathAsync(context),
                _ => null
            };
        }

        private Task<string?> FromHeaderAsync(HttpContext context)
        {
            var tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();

            if (!string.IsNullOrEmpty(tenantId))
                _logger.LogTrace("Tenant resolved from header: {TenantId}", tenantId);

            return Task.FromResult(tenantId);
        }

        private Task<string?> FromSubdomainAsync(HttpContext context)
        {
            try
            {
                var host = context.Request.Host.Host;
                if (string.IsNullOrEmpty(host))
                    return Task.FromResult<string?>(null);

                var parts = host.Split('.');

                // Skip if no subdomain (e.g., domain.com)
                if (parts.Length < 2)
                    return Task.FromResult<string?>(null);

                var subdomain = parts[0];

                // Skip common subdomains
                var excludedSubdomains = new[] { "www", "api", "admin", "app", "mail" };
                if (excludedSubdomains.Contains(subdomain))
                    return Task.FromResult<string?>(null);

                _logger.LogTrace("Tenant resolved from subdomain: {Subdomain}", subdomain);
                return Task.FromResult<string?>(subdomain);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting tenant from subdomain");
                return Task.FromResult<string?>(null);
            }
        }

        private Task<string?> FromJwtClaimAsync(HttpContext context)
        {
            var tenantClaim = context.User?.FindFirst("tenantId")?.Value
                              ?? context.User?.FindFirst("TenantId")?.Value
                              ?? context.User?.FindFirst("tenant_id")?.Value;

            if (!string.IsNullOrEmpty(tenantClaim))
                _logger.LogTrace("Tenant resolved from JWT claim: {TenantId}", tenantClaim);

            return Task.FromResult(tenantClaim);
        }

        private Task<string?> FromQueryStringAsync(HttpContext context)
        {
            var tenantId = context.Request.Query["tenantId"].FirstOrDefault()
                           ?? context.Request.Query["tenant_id"].FirstOrDefault();

            if (!string.IsNullOrEmpty(tenantId))
                _logger.LogTrace("Tenant resolved from query string: {TenantId}", tenantId);

            return Task.FromResult(tenantId);
        }

        private Task<string?> FromPathAsync(HttpContext context)
        {
            // Extract tenant from URL path: /api/{tenant}/...
            var path = context.Request.Path.Value;
            if (string.IsNullOrEmpty(path))
                return Task.FromResult<string?>(null);

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 1 && segments[0] == "api")
            {
                var tenantId = segments[1];
                _logger.LogTrace("Tenant resolved from path: {TenantId}", tenantId);
                return Task.FromResult<string?>(tenantId);
            }

            return Task.FromResult<string?>(null);
        }

        #endregion

        #region Tenant Resolution & Caching

        private async Task<TenantInfo?> GetOrResolveTenantAsync(ITenantService tenantService, string identifier)
        {
            // Check cache first (5 minutes sliding expiration)
            var cacheKey = $"tenant_{identifier}";
            if (_cache.TryGetValue<TenantInfo?>(cacheKey, out var cachedTenant))
                return cachedTenant;

            // Resolve from database/service
            var tenant = await tenantService.ResolveTenantAsync(identifier);

            if (tenant != null)
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                    .SetAbsoluteExpiration(TimeSpan.FromHours(1))
                    .SetPriority(CacheItemPriority.Normal)
                    .RegisterPostEvictionCallback((key, value, reason, state) =>
                    {
                        _logger.LogDebug("Tenant cache entry {Key} evicted. Reason: {Reason}", key, reason);
                    });

                _cache.Set(cacheKey, tenant, cacheOptions);
            }

            return tenant;
        }

        private async Task<bool> ValidateTenantAccessAsync(TenantInfo tenant, HttpContext context)
        {
            // Check if tenant is active
            if (!tenant.IsActive)
            {
                _logger.LogWarning("Tenant {TenantId} ({TenantName}) is inactive", tenant.Id, tenant.Name);
                return false;
            }

            // Check subscription expiration
            if (tenant.SubscriptionExpiryDate.HasValue && tenant.SubscriptionExpiryDate.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Tenant {TenantId} subscription expired on {ExpiryDate}",
                    tenant.Id, tenant.SubscriptionExpiryDate);
                return false;
            }

            // IP whitelist check (optional)
            if (tenant.AllowedIPs != null && tenant.AllowedIPs.Any())
            {
                var clientIp = context.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrEmpty(clientIp) && !tenant.AllowedIPs.Contains(clientIp))
                {
                    _logger.LogWarning("Tenant {TenantId} access from unauthorized IP: {ClientIp}",
                        tenant.Id, clientIp);
                    return false;
                }
            }

            return await Task.FromResult(true);
        }

        #endregion

        #region Context Management

        private async Task SetTenantContextAsync(HttpContext context, ICurrentUserService currentUser, TenantInfo tenant)
        {
            // Set tenant in HttpContext items for downstream middleware
            context.Items["Tenant"] = tenant;
            context.Items["TenantId"] = tenant.Id;
            context.Items["TenantIdentifier"] = tenant.Identifier;

            // Set tenant in current user service
            if (currentUser is CurrentUserService userService)
            {
                userService.SetTenant(tenant.Id);
            }

            // Add tenant to claims principal if needed
            var identity = context.User.Identity as ClaimsIdentity;
            if (identity != null && !context.User.HasClaim(c => c.Type == "TenantId"))
            {
                identity.AddClaim(new Claim("TenantId", tenant.Id.ToString()));
                identity.AddClaim(new Claim("TenantName", tenant.Name));
            }

            await Task.CompletedTask;
        }

        private void AddTenantHeaders(HttpContext context, TenantInfo tenant)
        {
            // Add tenant headers for response
            context.Response.Headers.Append("X-Tenant-Id", tenant.Id.ToString());
            context.Response.Headers.Append("X-Tenant-Name", tenant.Name);

            // For debugging in development only
            if (context.RequestServices.GetService<IWebHostEnvironment>()?.IsDevelopment() == true)
            {
                context.Response.Headers.Append("X-Tenant-Resolved", "true");
            }
        }

        #endregion

        #region Error Handling

        private async Task HandleTenantNotFoundAsync(HttpContext context, string? identifier, TenantResolutionStrategy? strategy)
        {
            _logger.LogWarning("Tenant not found for identifier: '{Identifier}' using strategy: {Strategy}",
                identifier ?? "null", strategy);

            // For API requests
            if (IsApiRequest(context))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($@"{{""error"":""Tenant not found"",""identifier"":""{identifier ?? "unknown"}""}}");
            }
            else // For UI requests
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Tenant not found. Please check your URL or contact support.");
            }
        }

        private async Task HandleTenantAccessDeniedAsync(HttpContext context, TenantInfo tenant)
        {
            _logger.LogWarning("Access denied for tenant {TenantId} ({TenantName})", tenant.Id, tenant.Name);

            if (IsApiRequest(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($@"{{""error"":""Access denied for tenant {tenant.Name}""}}");
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Access denied. Your tenant account is inactive or expired.");
            }
        }

        private async Task HandleTenantResolutionErrorAsync(HttpContext context, Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            if (IsApiRequest(context))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($@"{{""error"":""Tenant resolution failed"",""message"":""{ex.Message}""}}");
            }
            else
            {
                await context.Response.WriteAsync("An error occurred while resolving your tenant. Please try again later.");
            }
        }

        private static bool IsApiRequest(HttpContext context)
        {
            return context.Request.Path.StartsWithSegments("/api") ||
                   context.Request.Headers["Accept"].ToString().Contains("application/json") ||
                   context.Request.ContentType?.Contains("application/json") == true;
        }

        #endregion
    }

    #region Supporting Models & Enums

    public enum TenantResolutionStrategy
    {
        Header,
        Subdomain,
        JwtClaim,
        QueryString,
        Path
    }

    public class TenantInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime? SubscriptionExpiryDate { get; set; }
        public string[]? AllowedIPs { get; set; }
        public string? ConnectionString { get; set; }
        public string DatabaseName { get; set; } = string.Empty;
    }

    public interface ITenantService
    {
        Task<TenantInfo?> ResolveTenantAsync(string identifier);
        Task<TenantInfo?> GetTenantByIdAsync(int tenantId);
    }

    #endregion

    #region CurrentUserService Extension

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

    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private int? _manuallySetTenantId;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string UserId => GetClaimValue(ClaimTypes.NameIdentifier) ?? GetClaimValue("sub") ?? "anonymous";
        public string? Email => GetClaimValue(ClaimTypes.Email);
        public string? Username => GetClaimValue(ClaimTypes.Name) ?? GetClaimValue("username");

        public int? TenantId
        {
            get
            {
                // Manual override from middleware
                if (_manuallySetTenantId.HasValue)
                    return _manuallySetTenantId.Value;

                // From claim
                var tenantIdClaim = GetClaimValue("TenantId") ?? GetClaimValue("tenant_id");
                if (int.TryParse(tenantIdClaim, out var tenantId))
                    return tenantId;

                // From HttpContext items
                if (_httpContextAccessor.HttpContext?.Items["TenantId"] is int contextTenantId)
                    return contextTenantId;

                return null;
            }
        }

        public string[] Roles => GetClaimValues(ClaimTypes.Role);
        public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
        public bool IsSuperAdmin => Roles.Contains("SuperAdmin") || Roles.Contains("superadmin");

        public void SetTenant(int tenantId)
        {
            _manuallySetTenantId = tenantId;
        }

        public async Task<UserSessionInfo> GetCurrentUserSessionAsync()
        {
            // Implementation as before
            return await Task.FromResult(new UserSessionInfo());
        }

        private string? GetClaimValue(string claimType)
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
        }

        private string[] GetClaimValues(string claimType)
        {
            return _httpContextAccessor.HttpContext?.User?.FindAll(claimType).Select(c => c.Value).ToArray()
                   ?? Array.Empty<string>();
        }
    }

    #endregion
}