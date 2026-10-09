using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Inventory;

public sealed partial class InventoryCatalogService
{
    public async Task<ApiResponse<PagedResult<InventoryItemDto>>> GetItemsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<PagedResult<InventoryItemDto>>();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.InventoryItems.AsNoTracking().Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
        var term = Trim(search);
        if (term is { Length: > 100 }) return ApiResponse<PagedResult<InventoryItemDto>>.ErrorResponse("Search is too long.");
        if (term != null) query = query.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        var count = await query.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<InventoryItem>() :
            await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<InventoryItemDto>>.SuccessResponse(new PagedResult<InventoryItemDto>
        {
            Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<InventoryItemDto>> SaveItemAsync(long? itemId, SaveInventoryItemRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanWrite()) return Denied<InventoryItemDto>();
        if (request == null) return ApiResponse<InventoryItemDto>.ErrorResponse("Item details are required.");
        var code = Code(request.Code); var name = Trim(request.Name); var unit = Code(request.UnitCode);
        var category = Trim(request.CategoryCode);
        if (code.Length is < 1 or > 50 || name == null || name.Length > 200 ||
            unit.Length is < 1 or > 50 || category?.Length > 100 || request.ReorderLevel < 0 ||
            request.ReorderLevel > 999999999999m || itemId is <= 0)
            return ApiResponse<InventoryItemDto>.ErrorResponse("Invalid inventory item details.");
        var tenant = _user.TenantId;
        var now = _clock.GetUtcNow().UtcDateTime;
        try
        {
            InventoryItem entity;
            if (itemId.HasValue)
            {
                var found = await _db.InventoryItems.FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == itemId && !x.IsDeleted, cancellationToken);
                if (found == null) return ApiResponse<InventoryItemDto>.ErrorResponse("Item not found.", 404);
                if (!VersionMatches(found.RowVersion, request.RowVersion))
                    return ApiResponse<InventoryItemDto>.ErrorResponse("Item changed; reload before saving.", 409);
                entity = found;
            }
            else
            {
                var existing = await _db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Code == code && !x.IsDeleted, cancellationToken);
                if (existing != null)
                {
                    if (existing.Name == name && existing.UnitCode == unit && existing.CategoryCode == category &&
                        existing.IsStockTracked == request.IsStockTracked && existing.IsAssetTracked == request.IsAssetTracked &&
                        existing.ReorderLevel == request.ReorderLevel && existing.IsActive == request.IsActive)
                        return ApiResponse<InventoryItemDto>.SuccessResponse(Map(existing), "Item already exists.");
                    return ApiResponse<InventoryItemDto>.ErrorResponse("Item code is already in use.", 409);
                }
                entity = new InventoryItem { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                _db.InventoryItems.Add(entity);
            }
            if (await _db.InventoryItems.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.Code == code && x.Id != entity.Id && !x.IsDeleted, cancellationToken))
                return ApiResponse<InventoryItemDto>.ErrorResponse("Item code is already in use.", 409);
            entity.Code = code; entity.Name = name; entity.UnitCode = unit; entity.CategoryCode = category;
            entity.IsStockTracked = request.IsStockTracked; entity.IsAssetTracked = request.IsAssetTracked;
            entity.ReorderLevel = request.ReorderLevel; entity.IsActive = request.IsActive;
            if (itemId.HasValue) { entity.UpdatedAt = now; entity.UpdatedBy = _user.UserId; }
            await _db.SaveChangesAsync(cancellationToken);
            return new ApiResponse<InventoryItemDto> { Success = true, StatusCode = itemId.HasValue ? 200 : 201,
                Message = "Inventory item saved.", Data = Map(entity) };
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<InventoryItemDto>.ErrorResponse("Item changed; reload before saving.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Inventory item constraint conflict for tenant {TenantId}", tenant);
            return ApiResponse<InventoryItemDto>.ErrorResponse("Item conflicts with existing catalog records.", 409);
        }
    }
}
