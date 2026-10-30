using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IInventoryMovementService
{
    Task<ApiResponse<PagedResult<InventoryMovementDto>>> GetMovementsAsync(DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> GetMovementAsync(Guid movementReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> CreateMovementAsync(CreateInventoryMovementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> PostMovementAsync(Guid movementReference, PostInventoryMovementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ReceiveTransferAsync(Guid dispatchReference, ReceiveInventoryTransferRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ReverseMovementAsync(Guid movementReference, ReverseInventoryMovementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryReorderReportDto>> GetReorderAlertsAsync(long locationId, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryStockPositionReportDto>> GetStockPositionAsync(long locationId, DateTime? asOfUtc, string? search, bool includeZeroStock, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryBalanceDto>> GetItemBalanceAsync(long itemId, long locationId, DateTime? asOfUtc, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryItemHistoryReportDto>> GetItemHistoryAsync(long itemId, long locationId, DateTime? fromUtc, DateTime? toUtc, string? lot, int page, int pageSize, CancellationToken cancellationToken = default);
}
