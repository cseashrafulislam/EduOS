using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped billing, idempotent payment allocation, and bounded historical reports.</summary>
public interface IFeeBillingService
{
    Task<ApiResponse<FeeBillingOptionsDto>> GetOptionsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FeeStudentOptionDto>>> SearchStudentsAsync(string search, int take = 20, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeStructureDto>> SaveFeeStructureAsync(long? feeStructureId, SaveFeeStructureRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InvoiceBatchResultDto>> GenerateInvoicesAsync(GenerateStudentInvoiceBatchRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentPaymentDto>> CollectPaymentAsync(CreateStudentPaymentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLedgerDto>> GetStudentLedgerAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentInvoiceDto>>> GetStudentInvoicesAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentPaymentDto>>> GetStudentPaymentsAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
}
