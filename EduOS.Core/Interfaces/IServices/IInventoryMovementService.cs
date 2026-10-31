using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IInventoryMovementService
{
    Task<ApiResponse<PagedResult<ProcurementGoodsReceiptDto>>> GetGoodsReceiptsAsync(long? purchaseOrderId, string? state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<ProcurementGoodsReceiptDto>> GetGoodsReceiptAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<ProcurementGoodsReceiptDto>> CreateGoodsReceiptAsync(CreateProcurementGoodsReceiptRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ProcurementGoodsReceiptDto>> SubmitGoodsReceiptAsync(Guid reference, SubmitProcurementGoodsReceiptRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ProcurementGoodsReceiptDto>> ReviewGoodsReceiptAsync(Guid reference, ReviewProcurementGoodsReceiptRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<InventoryPhysicalCountDto>>> GetPhysicalCountsAsync(long? locationId, string? state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryPhysicalCountDto>> GetPhysicalCountAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryPhysicalCountDto>> StartPhysicalCountAsync(StartInventoryPhysicalCountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryPhysicalCountDto>> SubmitPhysicalCountAsync(Guid reference, SubmitInventoryPhysicalCountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryPhysicalCountDto>> ReviewPhysicalCountAsync(Guid reference, ReviewInventoryPhysicalCountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<InventoryMovementDto>>> GetMovementsAsync(DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> GetMovementAsync(Guid movementReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<InventoryReservationCoverageRowDto>>> GetReservationCoverageAsync(long locationId, string? search, bool overdueOnly, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<InventoryReservationRegisterRowDto>>> GetReservationRegisterAsync(long? locationId, string? search, int page, int pageSize, CancellationToken cancellationToken = default, bool includeClosed = false, bool overdueOnly = false);
    Task<ApiResponse<InventoryMovementDto>> CreateStockAdjustmentAsync(CreateInventoryStockAdjustmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> CreateMovementAsync(CreateInventoryMovementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> PostMovementAsync(Guid movementReference, PostInventoryMovementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ReserveMovementAsync(Guid movementReference, PostInventoryMovementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> FulfillReservedIssueAsync(Guid movementReference, FulfillInventoryReservedIssueRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> FulfillReservedTransferAsync(Guid movementReference, FulfillInventoryReservedTransferRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ScheduleReservationExpiryAsync(Guid movementReference, ScheduleInventoryReservationExpiryRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ExpireReservationAsync(Guid movementReference, ExpireInventoryReservationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ReleaseReservationAsync(Guid movementReference, ReleaseInventoryReservationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ReceiveTransferAsync(Guid dispatchReference, ReceiveInventoryTransferRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryMovementDto>> ReverseMovementAsync(Guid movementReference, ReverseInventoryMovementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<InventoryLotAgingRowDto>>> GetLotAgingAsync(long locationId, string? search, int minimumDays, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryReorderReportDto>> GetReorderAlertsAsync(long locationId, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryStockPositionReportDto>> GetStockPositionAsync(long locationId, DateTime? asOfUtc, string? search, bool includeZeroStock, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryBalanceDto>> GetItemBalanceAsync(long itemId, long locationId, DateTime? asOfUtc, CancellationToken cancellationToken = default);
    Task<ApiResponse<InventoryItemHistoryReportDto>> GetItemHistoryAsync(long itemId, long locationId, DateTime? fromUtc, DateTime? toUtc, string? lot, int page, int pageSize, CancellationToken cancellationToken = default);
}
