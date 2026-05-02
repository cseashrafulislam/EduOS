using AutoMapper;
using EduOS.Core.Entities.Base;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace EduOS.Service.Services
{
    /// <summary>
    /// Base class for all application services.
    /// Provides: tenant validation, audit fields, transactions, caching,
    /// logging helpers, and permission checks.
    /// </summary>
    public abstract class BaseService : IDisposable
    {
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly ICurrentUserService _currentUser;
        protected readonly ILogger _logger;
        protected readonly IMapper _mapper;
        protected readonly IMemoryCache _cache;

        // Track cache keys for pattern invalidation
        // Static so it survives across requests
        private static readonly ConcurrentDictionary<string, byte> _cacheKeys = new();

        protected BaseService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            ILogger logger,
            IMapper mapper,
            IMemoryCache cache)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        // ============================================================
        // TENANT VALIDATION (Security Critical)
        // ============================================================
        #region Tenant Validation

        /// <summary>
        /// Validates that the entity belongs to the current user's tenant.
        /// Throws UnauthorizedAccessException if cross-tenant access detected.
        /// SuperAdmin bypasses this check.
        /// </summary>
        protected void ValidateTenantAccess(long entityTenantId)
        {
            if (_currentUser.IsSuperAdmin) return;

            if (!_currentUser.IsAuthenticated)
                throw new UnauthorizedAccessException("User not authenticated");

            if (entityTenantId == 0)
                throw new InvalidOperationException("Entity has no tenant association");

            if (entityTenantId != _currentUser.TenantId)
            {
                _logger.LogWarning(
                    "Cross-tenant access attempt: User {UserId} (Tenant {UserTenant}) tried to access entity in Tenant {EntityTenant}",
                    _currentUser.UserId, _currentUser.TenantId, entityTenantId);
                throw new UnauthorizedAccessException("Cross-tenant access denied");
            }
        }

        /// <summary>
        /// Generic version - validates tenant access on any BaseTenantEntity
        /// </summary>
        protected void ValidateTenantAccess<T>(T entity) where T : BaseTenantEntity
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            ValidateTenantAccess(entity.TenantId);
        }

        /// <summary>
        /// Try-validate version - returns false instead of throwing.
        /// Use when you want to handle the case gracefully.
        /// </summary>
        protected bool CanAccessTenant(long entityTenantId)
        {
            if (_currentUser.IsSuperAdmin) return true;
            if (!_currentUser.IsAuthenticated) return false;
            if (entityTenantId == 0) return false;
            return entityTenantId == _currentUser.TenantId;
        }

        #endregion

        // ============================================================
        // AUDIT FIELDS (Auto-Set)
        // ============================================================
        #region Audit Fields

        /// <summary>
        /// Sets CreatedAt, CreatedBy, and TenantId (if applicable) for new entities.
        /// </summary>
        protected void SetAuditFieldsCreate<T>(T entity) where T : BaseEntity
        {
            if (entity == null) return;

            entity.CreatedAt = DateTime.UtcNow;
            entity.CreatedBy = _currentUser.UserId > 0 ? _currentUser.UserId : null;

            // Auto-assign tenant for new tenant-scoped entities
            if (entity is BaseTenantEntity tenantEntity && tenantEntity.TenantId == 0)
            {
                tenantEntity.TenantId = _currentUser.TenantId;
            }
        }

        /// <summary>
        /// Sets UpdatedAt and UpdatedBy when updating an entity.
        /// </summary>
        protected void SetAuditFieldsUpdate<T>(T entity) where T : BaseEntity
        {
            if (entity == null) return;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = _currentUser.UserId > 0 ? _currentUser.UserId : null;
        }

        /// <summary>
        /// Marks entity as soft-deleted with audit info.
        /// </summary>
        protected void SetAuditFieldsDelete<T>(T entity) where T : BaseEntity
        {
            if (entity == null) return;
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = _currentUser.UserId > 0 ? _currentUser.UserId : null;
        }

        #endregion

        // ============================================================
        // TRANSACTION MANAGEMENT
        // ============================================================
        #region Transactions

        protected Task BeginTransactionAsync() => _unitOfWork.BeginTransactionAsync();
        protected Task CommitTransactionAsync() => _unitOfWork.CommitTransactionAsync();
        protected Task RollbackTransactionAsync() => _unitOfWork.RollbackTransactionAsync();

        /// <summary>
        /// Helper to wrap an operation in a transaction with auto rollback on error.
        /// </summary>
        protected async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation)
        {
            await BeginTransactionAsync();
            try
            {
                var result = await operation();
                await CommitTransactionAsync();
                return result;
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        protected async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            await BeginTransactionAsync();
            try
            {
                await operation();
                await CommitTransactionAsync();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
        }

        #endregion

        // ============================================================
        // CACHING (using IMemoryCache - works without Redis)
        // ============================================================
        #region Caching

        /// <summary>
        /// Get value from cache. Returns default if not found or on error.
        /// </summary>
        protected Task<T?> GetFromCacheAsync<T>(string key)
        {
            try
            {
                if (_cache.TryGetValue<T>(key, out var value))
                    return Task.FromResult(value);

                return Task.FromResult<T?>(default);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache read failed for key {Key}", key);
                return Task.FromResult<T?>(default);
            }
        }

        /// <summary>
        /// Set cached value. Default expiration: 10 minutes.
        /// </summary>
        protected Task SetCacheAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            try
            {
                var options = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromMinutes(10),
                    Size = 1,  // required because of SizeLimit in Program.cs
                    Priority = CacheItemPriority.Normal
                };

                // Track key for pattern invalidation
                _cacheKeys.TryAdd(key, 0);

                // Remove from tracking when evicted
                options.RegisterPostEvictionCallback((k, _, _, _) =>
                {
                    _cacheKeys.TryRemove(k.ToString() ?? "", out _);
                });

                _cache.Set(key, value, options);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache write failed for key {Key}", key);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Remove a single cache entry.
        /// </summary>
        protected Task RemoveCacheAsync(string key)
        {
            try
            {
                _cache.Remove(key);
                _cacheKeys.TryRemove(key, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache remove failed for key {Key}", key);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Remove cache entries matching a pattern (e.g., "classes:list:1:*").
        /// Uses '*' as wildcard.
        /// </summary>
        protected Task RemovePatternCacheAsync(string pattern)
        {
            try
            {
                var prefix = pattern.TrimEnd('*');
                var matchingKeys = _cacheKeys.Keys
                    .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in matchingKeys)
                {
                    _cache.Remove(key);
                    _cacheKeys.TryRemove(key, out _);
                }

                _logger.LogDebug("Removed {Count} cache entries matching pattern {Pattern}",
                    matchingKeys.Count, pattern);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache pattern invalidation failed for {Pattern}", pattern);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Helper that gets from cache or executes loader and caches the result.
        /// </summary>
        protected async Task<T?> GetOrSetCacheAsync<T>(
            string key,
            Func<Task<T?>> loader,
            TimeSpan? expiration = null)
        {
            var cached = await GetFromCacheAsync<T>(key);
            if (cached != null) return cached;

            var fresh = await loader();
            if (fresh != null)
                await SetCacheAsync(key, fresh, expiration);

            return fresh;
        }

        /// <summary>
        /// Build a tenant-scoped cache key for safety.
        /// Always include tenant ID so different tenants don't share cache.
        /// </summary>
        protected string BuildCacheKey(string prefix, params object[] parts)
        {
            var tenantId = _currentUser.TenantId;
            var partsStr = string.Join(":", parts.Select(p => p?.ToString() ?? ""));
            return $"{prefix}:t{tenantId}:{partsStr}";
        }

        #endregion

        // ============================================================
        // LOGGING HELPERS
        // ============================================================
        #region Logging

        protected void LogEntityCreated<T>(T entity, long entityId) where T : class
        {
            _logger.LogInformation(
                "Entity {EntityType} created with ID {EntityId} by User {UserId} in Tenant {TenantId}",
                typeof(T).Name, entityId, _currentUser.UserId, _currentUser.TenantId);
        }

        protected void LogEntityUpdated<T>(T entity, long entityId) where T : class
        {
            _logger.LogInformation(
                "Entity {EntityType} updated with ID {EntityId} by User {UserId}",
                typeof(T).Name, entityId, _currentUser.UserId);
        }

        protected void LogEntityDeleted<T>(T entity, long entityId) where T : class
        {
            _logger.LogInformation(
                "Entity {EntityType} soft-deleted with ID {EntityId} by User {UserId}",
                typeof(T).Name, entityId, _currentUser.UserId);
        }

        protected void LogError(string message, Exception ex, params object[] args)
        {
            _logger.LogError(ex, message, args);
        }

        protected void LogWarning(string message, params object[] args)
        {
            _logger.LogWarning(message, args);
        }

        protected void LogInfo(string message, params object[] args)
        {
            _logger.LogInformation(message, args);
        }

        #endregion

        // ============================================================
        // PERMISSION & ROLE CHECKS
        // ============================================================
        #region Permission Checks

        /// <summary>
        /// Throws UnauthorizedAccessException if user doesn't have the role.
        /// SuperAdmin always passes.
        /// </summary>
        protected void RequireRole(string role)
        {
            if (_currentUser.IsSuperAdmin) return;

            if (!_currentUser.IsInRole(role))
            {
                _logger.LogWarning(
                    "User {UserId} attempted action requiring role '{Role}' but doesn't have it",
                    _currentUser.UserId, role);
                throw new UnauthorizedAccessException($"Role '{role}' required");
            }
        }

        /// <summary>
        /// Throws if user doesn't have any of the listed roles.
        /// </summary>
        protected void RequireAnyRole(params string[] roles)
        {
            if (_currentUser.IsSuperAdmin) return;

            if (!roles.Any(r => _currentUser.IsInRole(r)))
            {
                throw new UnauthorizedAccessException(
                    $"One of these roles required: {string.Join(", ", roles)}");
            }
        }

        /// <summary>
        /// Throws if user is not authenticated.
        /// </summary>
        protected void RequireAuthenticated()
        {
            if (!_currentUser.IsAuthenticated)
                throw new UnauthorizedAccessException("Authentication required");
        }

        /// <summary>
        /// Throws if user is not SuperAdmin.
        /// </summary>
        protected void RequireSuperAdmin()
        {
            if (!_currentUser.IsSuperAdmin)
                throw new UnauthorizedAccessException("SuperAdmin access required");
        }

        #endregion

        // ============================================================
        // DISPOSABLE PATTERN
        // ============================================================
        #region IDisposable

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // Clean up managed resources here if needed
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}