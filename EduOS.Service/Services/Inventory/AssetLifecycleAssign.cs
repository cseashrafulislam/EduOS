using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<AssetAssignmentDto>> AssignAssetAsync(AssignAssetRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite()) return Denied<AssetAssignmentDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.AssetReference == Guid.Empty ||
            request.AssignedOn == default || request.AssignedOn > Today)
            return Invalid<AssetAssignmentDto>("Asset, request ID and valid assignment date are required.");
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var asset = await VisibleAssets().FirstOrDefaultAsync(x => x.PublicId == request.AssetReference, ct);
            if (asset == null) return ApiResponse<AssetAssignmentDto>.ErrorResponse("Asset not found.", 404);
            if (!await CampusAllowedAsync(asset.CampusId, ct)) return Denied<AssetAssignmentDto>();
            if (!asset.CampusId.HasValue) return Invalid<AssetAssignmentDto>("Assign the asset to a campus before custody transfer.");
            var assignee = await ResolveAssigneeAsync(request, asset.CampusId.Value, ct);
            if (assignee.Error != null) return Invalid<AssetAssignmentDto>(assignee.Error);
            var prior = await _db.AssetAssignments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (prior != null)
            {
                if (prior.AssetId != asset.Id || prior.EmployeeId != assignee.EmployeeId ||
                    prior.StudentId != assignee.StudentId || prior.OrganizationUnitId != assignee.UnitId ||
                    prior.AssignedOn != request.AssignedOn)
                    return ApiResponse<AssetAssignmentDto>.ErrorResponse("Request ID was already used for a different assignment.", 409);
                return ApiResponse<AssetAssignmentDto>.SuccessResponse(await MapAssignmentAsync(prior, asset.PublicId, ct));
            }
            if (!asset.IsActive || asset.StateCode != "Available" || await _db.AssetAssignments.AnyAsync(x =>
                x.TenantId == Tenant && x.AssetId == asset.Id && !x.IsDeleted && x.ReturnedOn == null, ct))
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Asset is not available for assignment.", 409);
            var assignment = new AssetAssignment
            {
                TenantId = Tenant, ClientRequestId = request.ClientRequestId, AssetId = asset.Id,
                EmployeeId = assignee.EmployeeId, StudentId = assignee.StudentId, OrganizationUnitId = assignee.UnitId,
                AssignedOn = request.AssignedOn, StateCode = "Assigned", CreatedAt = Now, CreatedBy = _user.UserId
            };
            _db.AssetAssignments.Add(assignment);
            asset.StateCode = "Assigned"; asset.UpdatedAt = Now; asset.UpdatedBy = _user.UserId;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetAssignmentDto>.SuccessResponse(await MapAssignmentAsync(assignment, asset.PublicId, ct));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AssetAssignmentDto>.ErrorResponse("Asset changed; retry after reload.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Asset assignment constraint conflict for tenant {Tenant}", Tenant);
            return ApiResponse<AssetAssignmentDto>.ErrorResponse("Assignment conflict; reload before retrying.", 409);
        }
    }
}
