using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.Entities.Base;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace EduOS.Service.Services
{
    public abstract class BaseService : IDisposable
    {
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly ICurrentUserService _currentUser;
        protected readonly ILogger<BaseService> _logger;
        protected readonly IMapper _mapper;
        protected readonly IDistributedCache _cache;

        protected BaseService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            ILogger<BaseService> logger,
            IMapper mapper,
            IDistributedCache cache)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        #region Tenant Validation (Security Critical)

        protected void ValidateTenantAccess(int entityTenantId)
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

        protected void ValidateTenantAccess<T>(T entity) where T : BaseTenantEntity
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            ValidateTenantAccess(entity.TenantId);
        }

        #endregion

        #region Audit Fields (Auto-Set)

        protected void SetAuditFieldsCreate<T>(T entity) where T : BaseEntity
        {
            if (entity == null) return;
            var now = DateTime.UtcNow;
            entity.CreatedAt = now;
            entity.CreatedBy = _currentUser.UserId;
            if (entity is BaseTenantEntity tenantEntity && tenantEntity.TenantId == 0)
            {
                tenantEntity.TenantId = _currentUser.TenantId;
            }
        }

        protected void SetAuditFieldsUpdate<T>(T entity) where T : BaseEntity
        {
            if (entity == null) return;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = _currentUser.UserId;
        }

        #endregion

        #region Transaction Management

        protected async Task BeginTransactionAsync()
        {
            await _unitOfWork.BeginTransactionAsync();
        }

        protected async Task CommitTransactionAsync()
        {
            await _unitOfWork.CommitTransactionAsync();
        }

        protected async Task RollbackTransactionAsync()
        {
            await _unitOfWork.RollbackTransactionAsync();
        }

        #endregion

        #region Caching (Performance Critical)

        protected async Task<T?> GetFromCacheAsync<T>(string key)
        {
            try
            {
                var cachedData = await _cache.GetStringAsync(key);
                if (string.IsNullOrEmpty(cachedData)) return default;
                return JsonSerializer.Deserialize<T>(cachedData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache read failed for key {Key}", key);
                return default;
            }
        }

        protected async Task SetCacheAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            try
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromMinutes(10)
                };
                var serializedData = JsonSerializer.Serialize(value);
                await _cache.SetStringAsync(key, serializedData, options);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache write failed for key {Key}", key);
            }
        }

        protected async Task RemoveCacheAsync(string key)
        {
            try
            {
                await _cache.RemoveAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache remove failed for key {Key}", key);
            }
        }

        protected async Task RemovePatternCacheAsync(string pattern)
        {
            // For Redis, implement pattern-based invalidation
            // This requires Redis-specific implementation
            _logger.LogDebug("Cache pattern invalidation requested: {Pattern}", pattern);
        }

        #endregion

        #region Logging Helpers

        protected void LogEntityCreated<T>(T entity, int entityId) where T : class
        {
            _logger.LogInformation(
                "Entity {EntityType} created with ID {EntityId} by User {UserId} in Tenant {TenantId}",
                typeof(T).Name, entityId, _currentUser.UserId, _currentUser.TenantId);
        }

        protected void LogEntityUpdated<T>(T entity, int entityId) where T : class
        {
            _logger.LogInformation(
                "Entity {EntityType} updated with ID {EntityId} by User {UserId}",
                typeof(T).Name, entityId, _currentUser.UserId);
        }

        protected void LogEntityDeleted<T>(T entity, int entityId) where T : class
        {
            _logger.LogInformation(
                "Entity {EntityType} soft-deleted with ID {EntityId} by User {UserId}",
                typeof(T).Name, entityId, _currentUser.UserId);
        }

        protected void LogError(string message, Exception ex, params object[] args)
        {
            _logger.LogError(ex, message, args);
        }

        #endregion

        #region Permission Checks

        protected void RequirePermission(string permission)
        {
            if (!_currentUser.HasPermission(permission))
                throw new UnauthorizedAccessException($"Permission '{permission}' required");
        }

        protected void RequireRole(string role)
        {
            if (!_currentUser.HasRole(role))
                throw new UnauthorizedAccessException($"Role '{role}' required");
        }

        #endregion

        #region Disposable Pattern

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) { }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
