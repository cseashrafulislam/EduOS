using EduOS.Core.Common;
using EduOS.Core.DTOs.Inventory;

namespace EduOS.Core.Interfaces.IServices;

public interface IProcurementSupplierInvoiceService
{
    Task<ApiResponse<PagedResult<ProcurementSupplierInvoiceDto>>> ListAsync(long? purchaseOrderId,
        string? state, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierInvoiceDto>> GetAsync(Guid reference, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierInvoiceDto>> CreateAsync(CreateProcurementSupplierInvoiceRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierInvoiceDto>> SubmitAsync(Guid reference, SubmitProcurementSupplierInvoiceRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierInvoiceDto>> ReviewAsync(Guid reference, ReviewProcurementSupplierInvoiceRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierInvoiceDto>> RequestReversalAsync(Guid reference, RequestProcurementSupplierInvoiceReversal request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementSupplierInvoiceDto>> ReviewReversalAsync(Guid reference, ReviewProcurementSupplierInvoiceReversal request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ProcurementSupplierPayableAgingRowDto>>> AgingAsync(DateOnly? dueDateReference,
        long? supplierId, bool overdueOnly, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<SupplierSettlementOptionsDto>> SettlementOptionsAsync(Guid reference, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<SupplierSettlementInstructionDto>>> SettlementInstructionsAsync(Guid reference, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<SupplierSettlementInstructionDto>> RequestSettlementAsync(Guid reference, CreateSupplierSettlementInstructionRequest request, CancellationToken ct = default);
    Task<ApiResponse<SupplierSettlementInstructionDto>> WithdrawSettlementAsync(Guid reference, Guid instructionReference, WithdrawSupplierSettlementInstructionRequest request, CancellationToken ct = default);
    Task<ApiResponse<SupplierSettlementInstructionDto>> ReviewSettlementAsync(Guid reference, Guid instructionReference, ReviewSupplierSettlementInstructionRequest request, CancellationToken ct = default);
    Task<ApiResponse<ProcurementInvoicePostingOptionsDto>> PostingOptionsAsync(CancellationToken ct = default);
}
