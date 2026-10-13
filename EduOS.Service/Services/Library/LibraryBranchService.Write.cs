using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.Library;
public sealed partial class LibraryBranchService
{
    public async Task<ApiResponse<LibraryBranchDto>> SaveBranchAsync(long? id, SaveLibraryBranchRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<LibraryBranchDto>();
        if (request == null || id is <= 0 || request.CampusId <= 0 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
            string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50 ||
            request.Location?.Trim().Length > 500)
            return ApiResponse<LibraryBranchDto>.ErrorResponse("Valid campus, branch name and code are required.");
        var tenant = _user.TenantId;
        var name = request.Name.Trim(); var code = request.Code.Trim().ToUpperInvariant();
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, token))
                    return ApiResponse<LibraryBranchDto>.ErrorResponse("Active campus not found.", 404);
                var row = id.HasValue ? await _branches.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == id.Value && !x.IsDeleted, token) : null;
                if (id.HasValue && row == null) return ApiResponse<LibraryBranchDto>.ErrorResponse("Branch not found.", 404);
                if (row != null && !VersionMatches(row.RowVersion, request.RowVersion))
                    return ApiResponse<LibraryBranchDto>.ErrorResponse("Branch changed. Reload and retry.", 409);
                var duplicate = await _branches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.CampusId == request.CampusId && x.Code == code &&
                    !x.IsDeleted && (row == null || x.Id != row.Id), token);
                if (duplicate != null)
                {
                    if (row == null && duplicate.Name == name && duplicate.Location == Normalize(request.Location) &&
                        duplicate.IsActive == request.IsActive)
                        return ApiResponse<LibraryBranchDto>.SuccessResponse(Map(duplicate), "Branch already exists.");
                    return ApiResponse<LibraryBranchDto>.ErrorResponse("Branch code already exists on this campus.", 409);
                }
                return await ApplyBranchAsync(row, request, name, code, token);
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<LibraryBranchDto>.ErrorResponse("Branch changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Branch write conflict tenant {TenantId}", tenant);
            return ApiResponse<LibraryBranchDto>.ErrorResponse("Branch write conflict.", 409);
        }
    }
}