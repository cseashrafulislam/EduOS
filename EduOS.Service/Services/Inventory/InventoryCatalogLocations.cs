using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;
public sealed partial class InventoryCatalogService
{
    public async Task<ApiResponse<InventoryLocationDto>> SaveLocationAsync(long? locationId, SaveInventoryLocationRequestDto request, CancellationToken ct = default)
    {
        if (!CanWrite()) return Denied<InventoryLocationDto>();
        if (request == null) return ApiResponse<InventoryLocationDto>.ErrorResponse("Location details are required.");
        var code = Code(request.Code); var name = Trim(request.Name); var address = Trim(request.Address);
        if (code.Length is < 1 or > 50 || name == null || name.Length > 150 ||
            address?.Length > 500 || request.CampusId is <= 0 || locationId is <= 0)
            return ApiResponse<InventoryLocationDto>.ErrorResponse("Invalid inventory location details.");
        var tenant = _user.TenantId;
        if (request.CampusId.HasValue && !await _db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == request.CampusId && !x.IsDeleted && x.IsActive, ct))
            return ApiResponse<InventoryLocationDto>.ErrorResponse("Campus not found.", 404);
        var now = _clock.GetUtcNow().UtcDateTime;
        try
        {
            InventoryLocation entity;
            if (locationId.HasValue)
            {
                var found = await _db.InventoryLocations.FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == locationId && !x.IsDeleted, ct);
                if (found == null) return ApiResponse<InventoryLocationDto>.ErrorResponse("Location not found.", 404);
                if (!VersionMatches(found.RowVersion, request.RowVersion))
                    return ApiResponse<InventoryLocationDto>.ErrorResponse("Location changed; reload before saving.", 409);
                entity = found;
            }
            else
            {
                var existing = await _db.InventoryLocations.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Code == code && !x.IsDeleted, ct);
                if (existing != null)
                {
                    if (existing.Name == name && existing.Address == address && existing.CampusId == request.CampusId && existing.IsActive == request.IsActive)
                        return ApiResponse<InventoryLocationDto>.SuccessResponse(Map(existing), "Location already exists.");
                    return ApiResponse<InventoryLocationDto>.ErrorResponse("Location code is already in use.", 409);
                }
                entity = new InventoryLocation { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                _db.InventoryLocations.Add(entity);
            }
            if (await _db.InventoryLocations.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.Code == code && x.Id != entity.Id && !x.IsDeleted, ct))
                return ApiResponse<InventoryLocationDto>.ErrorResponse("Location code is already in use.", 409);
            entity.Code = code; entity.Name = name; entity.CampusId = request.CampusId; entity.IsActive = request.IsActive;
            if (!locationId.HasValue || request.Address != null) entity.Address = address;
            if (locationId.HasValue) { entity.UpdatedAt = now; entity.UpdatedBy = _user.UserId; }
            await _db.SaveChangesAsync(ct);
            return new ApiResponse<InventoryLocationDto> { Success = true, StatusCode = locationId.HasValue ? 200 : 201,
                Message = "Inventory location saved.", Data = Map(entity) };
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<InventoryLocationDto>.ErrorResponse("Location changed; reload before saving.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Inventory location constraint conflict for tenant {TenantId}", tenant);
            return ApiResponse<InventoryLocationDto>.ErrorResponse("Location conflicts with existing catalog records.", 409);
        }
    }
}
