using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.Entities.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService
{
    public async Task<ApiResponse<IReadOnlyList<NoticeCategoryDto>>> GetNoticeCategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<IReadOnlyList<NoticeCategoryDto>>.ErrorResponse("Tenant administrator access is required.", 403);
        var rows = await _categories.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(500).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<NoticeCategoryDto>>.SuccessResponse(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<NoticeCategoryDto>> SaveNoticeCategoryAsync(long? categoryId,
        SaveNoticeCategoryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<NoticeCategoryDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50
            || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category details are invalid.");
        var code = request.Code.Trim().ToUpperInvariant();
        try
        {
            var query = _categories.GetQueryable().Where(x => x.TenantId == _user.TenantId);
            var row = categoryId.HasValue
                ? await query.FirstOrDefaultAsync(x => x.Id == categoryId.Value, cancellationToken)
                : await query.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
            if (!categoryId.HasValue && row != null) return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category already exists.", 409);
            if (categoryId.HasValue && row == null) return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category not found.", 404);
            if (row != null && categoryId.HasValue && !MatchesVersion(row.RowVersion, request.RowVersion))
                return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category changed. Reload and retry.", 409);
            if (row != null && row.Code != code) return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category code cannot be changed.", 409);
            if (await query.AnyAsync(x => x.Code == code && x.Id != (row == null ? 0 : row.Id), cancellationToken))
                return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category code already exists.", 409);
            row ??= new NoticeCategory { TenantId = _user.TenantId, Code = code,
                CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId };
            row.Name = request.Name.Trim();
            row.IsActive = request.IsActive;
            if (row.Id == 0) await _categories.AddAsync(row);
            else { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId; }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<NoticeCategoryDto>.SuccessResponse(Map(row));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category changed. Reload and retry.", 409); }
        catch (DbUpdateException) { return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category conflicts with another update.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Notice category save failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<NoticeCategoryDto>.ErrorResponse("Category could not be saved.", 500);
        }
    }
}
