using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class AssetLifecycleService
{
    // The old and new custodians are changed in one serializable transaction. This does
    // not move warehouse stock, change campus or post a financial voucher.
    public async Task<ApiResponse<AssetAssignmentDto>> TransferAssetAsync(
        long assignmentId, TransferAssetRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite()) return Denied<AssetAssignmentDto>();
        var reason = Text(request?.Reason);
        if (assignmentId <= 0 || request == null || request.ClientRequestId == Guid.Empty ||
            request.TransferredOn == default || request.TransferredOn > Today ||
            reason.Length is < 1 or > 500 || string.IsNullOrWhiteSpace(request.RowVersion))
            return Invalid<AssetAssignmentDto>("Valid transfer, date, concurrency token and reason are required.");

        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var source = await _db.AssetAssignments.FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == assignmentId && !x.IsDeleted, ct);
            if (source == null)
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Assignment not found.", 404);

            var asset = await VisibleAssets().FirstOrDefaultAsync(x => x.Id == source.AssetId, ct);
            if (asset == null)
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Asset not found.", 404);
            if (!await CampusAllowedAsync(asset.CampusId, ct)) return Denied<AssetAssignmentDto>();
            if (!asset.CampusId.HasValue)
                return Invalid<AssetAssignmentDto>("Asset must belong to an active campus.");
            var target = await ResolveAssigneeAsync(new AssignAssetRequestDto
            {
                EmployeeReference = request.EmployeeReference,
                StudentReference = request.StudentReference,
                OrganizationUnitId = request.OrganizationUnitId,
                AssignedOn = request.TransferredOn
            }, asset.CampusId.Value, ct);
            if (target.Error != null) return Invalid<AssetAssignmentDto>(target.Error);

            var marker = "Transfer:" + request.ClientRequestId.ToString("N") + "|" + reason;
            var existing = await _db.AssetAssignments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, ct);
            if (existing != null)
            {
                if (existing.AssetId != asset.Id || existing.AssignedOn != request.TransferredOn ||
                    existing.EmployeeId != target.EmployeeId || existing.StudentId != target.StudentId ||
                    existing.OrganizationUnitId != target.UnitId ||
                    source.StateCode != "Transferred" || source.ReturnedOn != request.TransferredOn ||
                    source.Remarks == null || !source.Remarks.EndsWith(marker, StringComparison.Ordinal))
                    return ApiResponse<AssetAssignmentDto>.ErrorResponse(
                        "Request ID was used for another transfer.", 409);
                return ApiResponse<AssetAssignmentDto>.SuccessResponse(
                    await MapAssignmentAsync(existing, asset.PublicId, ct));
            }

            if (!Version(source.RowVersion, request.RowVersion))
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Custody changed; reload.", 409);
            if (!asset.IsActive || asset.StateCode != "Assigned" || source.StateCode != "Assigned" ||
                source.ReturnedOn.HasValue)
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Custody is no longer active.", 409);
            if (request.TransferredOn < source.AssignedOn)
                return Invalid<AssetAssignmentDto>("Transfer date cannot precede custody start.");
            if (source.EmployeeId == target.EmployeeId && source.StudentId == target.StudentId &&
                source.OrganizationUnitId == target.UnitId)
                return Invalid<AssetAssignmentDto>("Select a different custodian.");
            if (await _db.AssetAssignments.AnyAsync(x => x.TenantId == Tenant &&
                x.AssetId == asset.Id && x.Id != source.Id && !x.IsDeleted && x.ReturnedOn == null, ct))
                return ApiResponse<AssetAssignmentDto>.ErrorResponse("Multiple active custodians detected.", 409);

            var originalRemarks = Text(source.Remarks);
            if (originalRemarks.Length + marker.Length + (originalRemarks.Length > 0 ? 1 : 0) > 1000)
                return Invalid<AssetAssignmentDto>("Custody audit remarks have no remaining capacity.");

            source.ReturnedOn = request.TransferredOn;
            source.StateCode = "Transferred";
            source.Remarks = originalRemarks.Length == 0 ? marker : originalRemarks + "\n" + marker;
            source.UpdatedAt = Now;
            source.UpdatedBy = _user.UserId;

            var next = new AssetAssignment
            {
                TenantId = Tenant, ClientRequestId = request.ClientRequestId, AssetId = asset.Id,
                EmployeeId = target.EmployeeId, StudentId = target.StudentId,
                OrganizationUnitId = target.UnitId, AssignedOn = request.TransferredOn,
                StateCode = "Assigned", Remarks = "Transfer from assignment " + source.Id,
                CreatedAt = Now, CreatedBy = _user.UserId
            };
            _db.AssetAssignments.Add(next);
            asset.UpdatedAt = Now;
            asset.UpdatedBy = _user.UserId;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetAssignmentDto>.SuccessResponse(
                await MapAssignmentAsync(next, asset.PublicId, ct), "Custody transferred.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<AssetAssignmentDto>.ErrorResponse("Custody changed; reload.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Asset transfer constraint conflict for tenant {Tenant}", Tenant);
            return ApiResponse<AssetAssignmentDto>.ErrorResponse("Transfer conflict; reload before retrying.", 409);
        }
        catch (InvalidOperationException ex) when (ex.GetBaseException() is SqlException { Number: 1205 })
        {
            // EF can wrap a SQL deadlock in DbUpdateException and then InvalidOperationException.
            _logger.LogWarning(ex, "Asset transfer EF-wrapped deadlock for tenant {Tenant}", Tenant);
            return ApiResponse<AssetAssignmentDto>.ErrorResponse("Concurrent transfer; reload and retry.", 409);
        }
        catch (SqlException ex) when (ex.Number == 1205)
        {
            _logger.LogWarning(ex, "Asset transfer SQL deadlock for tenant {Tenant}", Tenant);
            return ApiResponse<AssetAssignmentDto>.ErrorResponse("Concurrent transfer; reload and retry.", 409);
        }
    }
}
