using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<AssetAssignmentDto>> ReturnAssetAsync(long assignmentId, ReturnAssetRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite()) return Denied<AssetAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.ReturnedOn == default || request.ReturnedOn > Today)
            return Invalid<AssetAssignmentDto>("Valid assignment and return date are required.");
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var assignment = await _db.AssetAssignments.FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == assignmentId && !x.IsDeleted, ct);
            if (assignment == null) return ApiResponse<AssetAssignmentDto>.ErrorResponse("Assignment not found.", 404);
            var asset = await VisibleAssets().FirstOrDefaultAsync(x => x.Id == assignment.AssetId, ct);
            if (asset == null) return ApiResponse<AssetAssignmentDto>.ErrorResponse("Asset not found.", 404);
            if (!await CampusAllowedAsync(asset.CampusId, ct)) return Denied<AssetAssignmentDto>();
            if (assignment.ReturnedOn.HasValue)
            {
                if (assignment.ReturnedOn == request.ReturnedOn && assignment.StateCode == "Returned")
                    return ApiResponse<AssetAssignmentDto>.SuccessResponse(await MapAssignmentAsync(assignment, asset.PublicId, ct));
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Assignment was already returned on another date.", 409);
            }
            if (!Version(assignment.RowVersion, request.RowVersion))
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Assignment changed; reload.", 409);
            if (assignment.StateCode != "Assigned" || asset.StateCode != "Assigned")
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Assignment is not open.", 409);
            if (request.ReturnedOn < assignment.AssignedOn)
                return Invalid<AssetAssignmentDto>("Return date cannot precede assignment date.");
            assignment.ReturnedOn = request.ReturnedOn; assignment.StateCode = "Returned";
            assignment.UpdatedAt = Now; assignment.UpdatedBy = _user.UserId;
            asset.StateCode = "Available"; asset.UpdatedAt = Now; asset.UpdatedBy = _user.UserId;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetAssignmentDto>.SuccessResponse(await MapAssignmentAsync(assignment, asset.PublicId, ct));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AssetAssignmentDto>.ErrorResponse("Assignment changed; reload.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Asset return conflict for tenant {Tenant}", Tenant);
            return ApiResponse<AssetAssignmentDto>.ErrorResponse("Return conflict; reload before retrying.", 409);
        }
    }
}
