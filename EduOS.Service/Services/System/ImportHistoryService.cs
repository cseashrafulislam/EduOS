using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services;

public sealed class ImportHistoryService : IImportHistoryService
{
    private readonly IGenericRepository<ImportLog> _imports;
    private readonly IGenericRepository<ImportLogItem> _rows;
    private readonly ICurrentUserService _user;
    private readonly ILogger<ImportHistoryService> _logger;

    public ImportHistoryService(IGenericRepository<ImportLog> imports, IGenericRepository<ImportLogItem> rows,
        ICurrentUserService user, ILogger<ImportHistoryService> logger)
    {
        _imports = imports; _rows = rows; _user = user; _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<ImportLogDto>>> GetImportsAsync(string? importType,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<PagedResult<ImportLogDto>>.ErrorResponse("Import history access denied.", 403);
        if (!ValidPage(page, pageSize) || importType?.Trim().Length > 100)
            return ApiResponse<PagedResult<ImportLogDto>>.ErrorResponse("Invalid import search parameters.", 400);
        try
        {
            var query = _imports.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
            if (!string.IsNullOrWhiteSpace(importType))
            {
                var type = importType.Trim();
                query = query.Where(x => x.ImportType == type);
            }
            var count = await query.CountAsync(cancellationToken);
            var offset = ((long)page - 1) * pageSize;
            var items = offset > int.MaxValue ? new List<ImportLogDto>() : await query
                .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
                .Skip((int)offset).Take(pageSize).Select(x => new ImportLogDto
                {
                    Id = x.Id, Reference = x.PublicId, ImportType = x.ImportType,
                    FileAssetId = x.FileAssetId, TotalRows = x.TotalRows,
                    SuccessRows = x.SuccessRows, FailedRows = x.FailedRows,
                    StartedAt = x.StartedAt, CompletedAt = x.CompletedAt,
                    ErrorSummary = x.ErrorSummary
                }).ToListAsync(cancellationToken);
            return ApiResponse<PagedResult<ImportLogDto>>.SuccessResponse(new PagedResult<ImportLogDto>
            {
                Items = items, TotalCount = count, Page = page, PageSize = pageSize
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import history lookup failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<ImportLogDto>>.ErrorResponse("Unable to load import history.", 500);
        }
    }

    public async Task<ApiResponse<ImportLogDto>> GetImportAsync(Guid importReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<ImportLogDto>.ErrorResponse("Import history access denied.", 403);
        if (importReference == Guid.Empty) return ApiResponse<ImportLogDto>.ErrorResponse("Invalid import reference.", 400);
        try
        {
            var item = await _imports.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted && x.PublicId == importReference)
                .Select(x => new ImportLogDto
                {
                    Id = x.Id, Reference = x.PublicId, ImportType = x.ImportType,
                    FileAssetId = x.FileAssetId, TotalRows = x.TotalRows,
                    SuccessRows = x.SuccessRows, FailedRows = x.FailedRows,
                    StartedAt = x.StartedAt, CompletedAt = x.CompletedAt,
                    ErrorSummary = x.ErrorSummary
                }).FirstOrDefaultAsync(cancellationToken);
            return item == null ? ApiResponse<ImportLogDto>.ErrorResponse("Import not found.", 404)
                : ApiResponse<ImportLogDto>.SuccessResponse(item);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import detail lookup failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<ImportLogDto>.ErrorResponse("Unable to load import details.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<ImportLogItemDto>>> GetImportRowsAsync(Guid importReference,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<PagedResult<ImportLogItemDto>>.ErrorResponse("Import history access denied.", 403);
        if (importReference == Guid.Empty || !ValidPage(page, pageSize))
            return ApiResponse<PagedResult<ImportLogItemDto>>.ErrorResponse("Invalid import row query.", 400);
        try
        {
            var importId = await _imports.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted && x.PublicId == importReference)
                .Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);
            if (!importId.HasValue) return ApiResponse<PagedResult<ImportLogItemDto>>.ErrorResponse("Import not found.", 404);
            var query = _rows.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted && x.ImportLogId == importId.Value);
            var count = await query.CountAsync(cancellationToken);
            var offset = ((long)page - 1) * pageSize;
            var items = offset > int.MaxValue ? new List<ImportLogItemDto>() : await query
                .OrderBy(x => x.RowNumber).ThenBy(x => x.Id)
                .Skip((int)offset).Take(pageSize).Select(x => new ImportLogItemDto
                {
                    Id = x.Id, ImportLogId = x.ImportLogId, RowNumber = x.RowNumber,
                    BusinessKey = x.BusinessKey, IsSuccess = x.IsSuccess, ErrorMessage = x.ErrorMessage
                }).ToListAsync(cancellationToken);
            return ApiResponse<PagedResult<ImportLogItemDto>>.SuccessResponse(new PagedResult<ImportLogItemDto>
            {
                Items = items, TotalCount = count, Page = page, PageSize = pageSize
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import row lookup failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<ImportLogItemDto>>.ErrorResponse("Unable to load import rows.", 500);
        }
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0
        && (_user.IsTenantAdmin || _user.IsInRole("Principal"));
    private static bool ValidPage(int page, int pageSize) => page >= 1 && pageSize >= 1 && pageSize <= 100;
}
