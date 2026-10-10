using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IInventoryCatalogService
{
    Task<ApiResponse<PagedResult<InventoryItemDto>>> GetItemsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryItemDto>> SaveItemAsync(long? itemId, SaveInventoryItemRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<InventoryLocationDto>>> GetLocationsAsync(long? campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<InventoryLocationDto>>> GetLocationsPageAsync(int page, int pageSize, long? campusId, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryLocationDto>> SaveLocationAsync(long? locationId, SaveInventoryLocationRequestDto request, CancellationToken cancellationToken = default);
}
