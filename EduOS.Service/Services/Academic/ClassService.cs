using AutoMapper;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
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
            ILogger<BaseService> logger,
            IMapper mapper,
            IDistributedCache cache,
            IClassRepository classRepository)
            : base(unitOfWork, currentUser, logger, mapper, cache)
        {
            _classRepository = classRepository;
        }

        public async Task<ApiResponse<PagedResult<ClassDto>>> GetAllAsync(ClassListFilterDto filter)
        {
            try
            {
                // Build cache key
                var cacheKey = $"{CACHE_PREFIX}:list:{_currentUser.TenantId}:{filter.Page}:{filter.PageSize}";

                // Try cache first
                var cachedResult = await GetFromCacheAsync<PagedResult<ClassDto>>(cacheKey);
                if (cachedResult != null)
                {
                    _logger.LogDebug("Cache hit for classes list");
                    return ApiResponse<PagedResult<ClassDto>>.SuccessResponse(cachedResult);
                }

                // Build query with tenant filter
                var query = _classRepository.GetQueryable()
                    .Where(c => c.TenantId == _currentUser.TenantId);

                // Apply filters
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

                // Cache for 5 minutes
                await SetCacheAsync(cacheKey, result, TimeSpan.FromMinutes(5));

                return ApiResponse<PagedResult<ClassDto>>.SuccessResponse(result);
            }
            catch (Exception ex)
            {
                LogError("Error fetching classes", ex);
                return ApiResponse<PagedResult<ClassDto>>.ErrorResponse("Failed to fetch classes", 500);
            }
        }

        public async Task<ApiResponse<ClassDto>> GetByIdAsync(int id)
        {
            try
            {
                var cacheKey = $"{CACHE_PREFIX}:id:{_currentUser.TenantId}:{id}";

                // Try cache first
                var cached = await GetFromCacheAsync<ClassDto>(cacheKey);
                if (cached != null)
                {
                    return ApiResponse<ClassDto>.SuccessResponse(cached);
                }

                var entity = await _classRepository.GetByIdAsync(id);

                if (entity == null || entity.TenantId != _currentUser.TenantId)
                    return ApiResponse<ClassDto>.ErrorResponse("Class not found", 404);

                var dto = _mapper.Map<ClassDto>(entity);

                // Cache for 10 minutes
                await SetCacheAsync(cacheKey, dto, TimeSpan.FromMinutes(10));

                return ApiResponse<ClassDto>.SuccessResponse(dto);
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
                await BeginTransactionAsync();

                // Check duplicate
                var exists = await _classRepository.AnyAsync(
                    c => c.Name == dto.Name && c.TenantId == _currentUser.TenantId);

                if (exists)
                {
                    await RollbackTransactionAsync();
                    return ApiResponse<ClassDto>.ErrorResponse("Class name already exists", 400);
                }

                var entity = _mapper.Map<Class>(dto);
                SetAuditFieldsCreate(entity);

                await _classRepository.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();
                await CommitTransactionAsync();

                // Invalidate cache
                await RemovePatternCacheAsync($"{CACHE_PREFIX}:list:{_currentUser.TenantId}:*");

                LogEntityCreated(entity, entity.Id);

                var resultDto = _mapper.Map<ClassDto>(entity);
                return ApiResponse<ClassDto>.SuccessResponse(resultDto, "Class created successfully");
            }
            catch (Exception ex)
            {
                await RollbackTransactionAsync();
                LogError("Error creating class", ex);
                return ApiResponse<ClassDto>.ErrorResponse("Failed to create class", 500);
            }
        }

        public async Task<ApiResponse<ClassDto>> UpdateAsync(int id, ClassUpdateDto dto)
        {
            try
            {
                await BeginTransactionAsync();

                var entity = await _classRepository.GetByIdAsync(id);

                if (entity == null || entity.TenantId != _currentUser.TenantId)
                {
                    await RollbackTransactionAsync();
                    return ApiResponse<ClassDto>.ErrorResponse("Class not found", 404);
                }

                var exists = await _classRepository.AnyAsync(
                    c => c.Name == dto.Name && c.TenantId == _currentUser.TenantId && c.Id != id);

                if (exists)
                {
                    await RollbackTransactionAsync();
                    return ApiResponse<ClassDto>.ErrorResponse("Class name already exists", 400);
                }

                _mapper.Map(dto, entity);
                SetAuditFieldsUpdate(entity);

                _classRepository.Update(entity);
                await _unitOfWork.SaveChangesAsync();
                await CommitTransactionAsync();

                // Invalidate cache
                await RemoveCacheAsync($"{CACHE_PREFIX}:id:{_currentUser.TenantId}:{id}");
                await RemovePatternCacheAsync($"{CACHE_PREFIX}:list:{_currentUser.TenantId}:*");

                LogEntityUpdated(entity, entity.Id);

                var resultDto = _mapper.Map<ClassDto>(entity);
                return ApiResponse<ClassDto>.SuccessResponse(resultDto, "Class updated successfully");
            }
            catch (Exception ex)
            {
                await RollbackTransactionAsync();
                LogError("Error updating class {Id}", ex, id);
                return ApiResponse<ClassDto>.ErrorResponse("Failed to update class", 500);
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(int id)
        {
            try
            {
                await BeginTransactionAsync();

                var entity = await _classRepository.GetByIdAsync(id);

                if (entity == null || entity.TenantId != _currentUser.TenantId)
                {
                    await RollbackTransactionAsync();
                    return ApiResponse<bool>.ErrorResponse("Class not found", 404);
                }

                entity.IsDeleted = true;
                SetAuditFieldsUpdate(entity);

                _classRepository.Update(entity);
                await _unitOfWork.SaveChangesAsync();
                await CommitTransactionAsync();

                // Invalidate cache
                await RemoveCacheAsync($"{CACHE_PREFIX}:id:{_currentUser.TenantId}:{id}");
                await RemovePatternCacheAsync($"{CACHE_PREFIX}:list:{_currentUser.TenantId}:*");

                LogEntityDeleted(entity, entity.Id);

                return ApiResponse<bool>.SuccessResponse(true, "Class deleted successfully");
            }
            catch (Exception ex)
            {
                await RollbackTransactionAsync();
                LogError("Error deleting class {Id}", ex, id);
                return ApiResponse<bool>.ErrorResponse("Failed to delete class", 500);
            }
        }

        public async Task<ApiResponse<List<ClassDto>>> GetActiveClassesAsync()
        {
            try
            {
                var cacheKey = $"{CACHE_PREFIX}:active:{_currentUser.TenantId}";

                var cached = await GetFromCacheAsync<List<ClassDto>>(cacheKey);
                if (cached != null)
                {
                    return ApiResponse<List<ClassDto>>.SuccessResponse(cached);
                }

                var entities = await _classRepository.FindAsync(
                    c => c.TenantId == _currentUser.TenantId && c.IsActive,
                    c => c.NumericValue
                );

                var dtos = _mapper.Map<List<ClassDto>>(entities);

                // Cache for 15 minutes (active classes change less frequently)
                await SetCacheAsync(cacheKey, dtos, TimeSpan.FromMinutes(15));

                return ApiResponse<List<ClassDto>>.SuccessResponse(dtos);
            }
            catch (Exception ex)
            {
                LogError("Error getting active classes", ex);
                return ApiResponse<List<ClassDto>>.ErrorResponse("Failed to get active classes", 500);
            }
        }
    }
}
