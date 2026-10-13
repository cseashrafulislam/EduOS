using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace EduOS.Service.Services.Inventory;
public sealed partial class AssetLifecycleService
{
    public async Task<ApiResponse<AssetMaintenanceDto>> RecordMaintenanceAsync(RecordAssetMaintenanceRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite()) return Denied<AssetMaintenanceDto>();
        if (request == null || request.AssetReference == Guid.Empty || request.ClientRequestId == Guid.Empty ||
            request.MaintenanceDate == default || request.MaintenanceDate > Today || !Money(request.Cost) ||
            Text(request.WorkDescription).Length is < 1 or > 200 || Text(request.ServiceProvider).Length > 200 ||
            (request.NextMaintenanceDate.HasValue && request.NextMaintenanceDate < request.MaintenanceDate))
            return Invalid<AssetMaintenanceDto>("Invalid maintenance details or monetary precision.");
        // Until Platform adds a dedicated indexed ClientRequestId, retain the request token in remarks.
        var marker = "RequestId:" + request.ClientRequestId.ToString("N") + ";";
        var remarks = marker + Text(request.Remarks);
        if (remarks.Length > 1000) return Invalid<AssetMaintenanceDto>("Remarks exceed the supported length.");
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var asset = await VisibleAssets().FirstOrDefaultAsync(x => x.PublicId == request.AssetReference, ct);
            if (asset == null) return ApiResponse<AssetMaintenanceDto>.ErrorResponse("Asset not found.", 404);
            if (!await CampusAllowedAsync(asset.CampusId, ct)) return Denied<AssetMaintenanceDto>();
            var existing = await _db.AssetMaintenances.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.Remarks != null && x.Remarks.StartsWith(marker), ct);
            if (existing != null)
            {
                if (existing.AssetId != asset.Id || existing.MaintenanceDate != request.MaintenanceDate ||
                    existing.WorkDescription != Text(request.WorkDescription) || existing.Cost != request.Cost ||
                    existing.ServiceProvider != (Text(request.ServiceProvider).Length == 0 ? null : Text(request.ServiceProvider)) ||
                    existing.NextMaintenanceDate != request.NextMaintenanceDate || existing.Remarks != remarks)
                    return ApiResponse<AssetMaintenanceDto>.ErrorResponse("Request ID was already used for different maintenance.", 409);
                return ApiResponse<AssetMaintenanceDto>.SuccessResponse(Map(existing, asset.PublicId));
            }
            if (!asset.IsActive || asset.StateCode != "Available")
                return ApiResponse<AssetMaintenanceDto>.ErrorResponse("Return the asset before recording maintenance.", 409);
            var entry = new AssetMaintenance
            {
                TenantId = Tenant, AssetId = asset.Id, MaintenanceDate = request.MaintenanceDate,
                WorkDescription = Text(request.WorkDescription), Cost = request.Cost,
                ServiceProvider = string.IsNullOrWhiteSpace(request.ServiceProvider) ? null : Text(request.ServiceProvider),
                NextMaintenanceDate = request.NextMaintenanceDate, Remarks = remarks,
                CreatedAt = Now, CreatedBy = _user.UserId
            };
            _db.AssetMaintenances.Add(entry);
            asset.UpdatedAt = Now; asset.UpdatedBy = _user.UserId;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<AssetMaintenanceDto>.SuccessResponse(Map(entry, asset.PublicId));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<AssetMaintenanceDto>.ErrorResponse("Asset changed; reload.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Asset maintenance conflict for tenant {Tenant}", Tenant);
            return ApiResponse<AssetMaintenanceDto>.ErrorResponse("Maintenance conflict; retry after reload.", 409);
        }
    }
}
