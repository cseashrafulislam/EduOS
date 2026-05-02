using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Academic
{
    public class ClassService : BaseService, IClassService
    {
        private readonly IClassRepository _classRepository;
        private const string CACHE_PREFIX = "classes";

        public ClassService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            ILogger<ClassService> logger,         // Specific logger type for clarity
            IMapper mapper,
            IMemoryCache cache,                    // IMemoryCache instead of IDistributedCache
            IClassRepository classRepository)
            : base(unitOfWork, currentUser, logger, mapper, cache)
        {
            _classRepository = classRepository;
        }

        public async Task<ApiResponse<PagedResult<ClassDto>>> GetAllAsync(ClassListFilterDto filter)
        {
            try
            {
                // Tenant-scoped cache key
                var cacheKey = BuildCacheKey(CACHE_PREFIX, "list", filter.Page, filter.PageSize,
                    filter.SearchTerm ?? "", filter.IsActive?.ToString() ?? "");

                var cached = await GetFromCacheAsync<PagedResult<ClassDto>>(cacheKey);
                if (cached != null)
                    return ApiResponse<PagedResult<ClassDto>>.SuccessResponse(cached);

                var query = _classRepository.GetQueryable()
                    .Where(c => c.TenantId == _currentUser.TenantId);

                if (!string.IsNullOrEmpty(filter.SearchTerm))
                    query = query.Where(c => c.Name.Contains(filter.SearchTerm));

                if (filter.IsActive.HasValue)
                    query = query.Where(c => c.IsActive == filter.IsActive.Value);

                var totalCount = await query.CountAsync();
                var items = await query
                    .OrderBy(c => c.NumericValue)
                    .Skip((filter.Page - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToListAsync();

                var dtos = _mapper.Map<List<ClassDto>>(items);
                var result = new PagedResult<ClassDto>
                {
                    Items = dtos,
                    TotalCount = totalCount,
                    Page = filter.Page,
                    PageSize = filter.PageSize
                };

                await SetCacheAsync(cacheKey, result, TimeSpan.FromMinutes(5));
                return ApiResponse<PagedResult<ClassDto>>.SuccessResponse(result);
            }
            catch (Exception ex)
            {
                LogError("Error fetching classes", ex);
                return ApiResponse<PagedResult<ClassDto>>.ErrorResponse("Failed to fetch classes", 500);
            }
        }

        public async Task<ApiResponse<ClassDto>> GetByIdAsync(long id)
        {
            try
            {
                var cacheKey = BuildCacheKey(CACHE_PREFIX, "id", id);
                var cached = await GetFromCacheAsync<ClassDto>(cacheKey);
                if (cached != null)
                    return ApiResponse<ClassDto>.SuccessResponse(cached);

                var entity = await _classRepository.GetByIdAsync(id);
                if (entity == null)
                    return ApiResponse<ClassDto>.ErrorResponse("Class not found", 404);

                // Security: validate tenant access
                ValidateTenantAccess(entity);

                var dto = _mapper.Map<ClassDto>(entity);
                await SetCacheAsync(cacheKey, dto, TimeSpan.FromMinutes(10));

                return ApiResponse<ClassDto>.SuccessResponse(dto);
            }
            catch (UnauthorizedAccessException ex)
            {
                return ApiResponse<ClassDto>.ErrorResponse(ex.Message, 403);
            }
            catch (Exception ex)
            {
                LogError("Error getting class {Id}", ex, id);
                return ApiResponse<ClassDto>.ErrorResponse("Failed to get class", 500);
            }
        }

        public async Task<ApiResponse<ClassDto>> CreateAsync(ClassCreateDto dto)
        {
            try
            {
                return await ExecuteInTransactionAsync(async () =>
                {
                    var exists = await _classRepository.AnyAsync(
                        c => c.Name == dto.Name && c.TenantId == _currentUser.TenantId);

                    if (exists)
                        return ApiResponse<ClassDto>.ErrorResponse("Class name already exists", 400);

                    var entity = _mapper.Map<Class>(dto);
                    SetAuditFieldsCreate(entity);  // Sets TenantId, CreatedAt, CreatedBy

                    await _classRepository.AddAsync(entity);
                    await _unitOfWork.SaveChangesAsync();

                    // Invalidate list cache
                    await RemovePatternCacheAsync(BuildCacheKey(CACHE_PREFIX, "list", "*"));

                    LogEntityCreated(entity, entity.Id);

                    var resultDto = _mapper.Map<ClassDto>(entity);
                    return ApiResponse<ClassDto>.SuccessResponse(resultDto, "Class created successfully");
                });
            }
            catch (Exception ex)
            {
                LogError("Error creating class", ex);
                return ApiResponse<ClassDto>.ErrorResponse("Failed to create class", 500);
            }
        }

        public async Task<ApiResponse<ClassDto>> UpdateAsync(long id, ClassUpdateDto dto)
        {
            try
            {
                return await ExecuteInTransactionAsync(async () =>
                {
                    var entity = await _classRepository.GetByIdAsync(id);
                    if (entity == null)
                        return ApiResponse<ClassDto>.ErrorResponse("Class not found", 404);

                    // Security: validate tenant access
                    ValidateTenantAccess(entity);

                    var exists = await _classRepository.AnyAsync(
                        c => c.Name == dto.Name && c.TenantId == _currentUser.TenantId && c.Id != id);

                    if (exists)
                        return ApiResponse<ClassDto>.ErrorResponse("Class name already exists", 400);

                    _mapper.Map(dto, entity);
                    SetAuditFieldsUpdate(entity);

                    _classRepository.Update(entity);
                    await _unitOfWork.SaveChangesAsync();

                    // Invalidate caches
                    await RemoveCacheAsync(BuildCacheKey(CACHE_PREFIX, "id", id));
                    await RemovePatternCacheAsync(BuildCacheKey(CACHE_PREFIX, "list", "*"));

                    LogEntityUpdated(entity, entity.Id);

                    var resultDto = _mapper.Map<ClassDto>(entity);
                    return ApiResponse<ClassDto>.SuccessResponse(resultDto, "Class updated successfully");
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return ApiResponse<ClassDto>.ErrorResponse(ex.Message, 403);
            }
            catch (Exception ex)
            {
                LogError("Error updating class {Id}", ex, id);
                return ApiResponse<ClassDto>.ErrorResponse("Failed to update class", 500);
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(long id)
        {
            try
            {
                return await ExecuteInTransactionAsync(async () =>
                {
                    var entity = await _classRepository.GetByIdAsync(id);
                    if (entity == null)
                        return ApiResponse<bool>.ErrorResponse("Class not found", 404);

                    ValidateTenantAccess(entity);

                    SetAuditFieldsDelete(entity);  // Soft delete

                    _classRepository.Update(entity);
                    await _unitOfWork.SaveChangesAsync();

                    await RemoveCacheAsync(BuildCacheKey(CACHE_PREFIX, "id", id));
                    await RemovePatternCacheAsync(BuildCacheKey(CACHE_PREFIX, "list", "*"));

                    LogEntityDeleted(entity, entity.Id);

                    return ApiResponse<bool>.SuccessResponse(true, "Class deleted successfully");
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return ApiResponse<bool>.ErrorResponse(ex.Message, 403);
            }
            catch (Exception ex)
            {
                LogError("Error deleting class {Id}", ex, id);
                return ApiResponse<bool>.ErrorResponse("Failed to delete class", 500);
            }
        }

        public async Task<ApiResponse<List<ClassDto>>> GetActiveClassesAsync()
        {
            try
            {
                var cacheKey = BuildCacheKey(CACHE_PREFIX, "active");

                var dtos = await GetOrSetCacheAsync(
                    cacheKey,
                    async () =>
                    {
                        var entities = await _classRepository.FindAsync(
                            c => c.TenantId == _currentUser.TenantId && c.IsActive,
                            c => c.NumericValue);

                        return _mapper.Map<List<ClassDto>>(entities);
                    },
                    TimeSpan.FromMinutes(15));

                return ApiResponse<List<ClassDto>>.SuccessResponse(dtos ?? new List<ClassDto>());
            }
            catch (Exception ex)
            {
                LogError("Error getting active classes", ex);
                return ApiResponse<List<ClassDto>>.ErrorResponse("Failed to get active classes", 500);
            }
        }
    }
}