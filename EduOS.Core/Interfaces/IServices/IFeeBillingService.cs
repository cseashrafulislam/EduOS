using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped billing, idempotent payment allocation, and bounded historical reports.</summary>
public interface IFeeBillingService
{
    Task<ApiResponse<PagedResult<StudentFeeInstallmentPlanDto>>> GetInstallmentPlansAsync(long? campusId = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFeeInstallmentPlanDto>> GetInstallmentPlanAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFeeInstallmentBillingAuditDto>> GetInstallmentBillingAuditAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFeeInstallmentPlanDto>> CreateInstallmentPlanAsync(CreateStudentFeeInstallmentPlanDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFeeInstallmentPlanDto>> ReplaceInstallmentPlanAsync(Guid reference, ReplaceStudentFeeInstallmentPlanDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFeeInstallmentMonthInvoiceDto>> IssueInstallmentMonthAsync(Guid reference, IssueStudentFeeInstallmentMonthDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFeeInstallmentPlanDto>> TransitionInstallmentPlanAsync(Guid reference, int actionCode, StudentFeeInstallmentTransitionDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeDiscountAttributionReviewReportDto>> GetDiscountAttributionReviewAsync(FeeDiscountAttributionReviewQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeInvoiceCoverageReportDto>> GetInvoiceCoverageAsync(FeeInvoiceCoverageQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeInstallmentProjectionReportDto>> GetInstallmentProjectionAsync(
        FeeInstallmentProjectionQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<DiscountRuleDto>>> GetDiscountRulesAsync(int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<DiscountRuleDto>> SaveDiscountRuleAsync(long? discountRuleId, SaveDiscountRuleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<FeeStructureRegisterDto>>> GetStructureRegisterAsync(long? campusId = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeStructureDto>> GetStructureAsync(long id, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeStructureDto>> CloseFeeStructureAsync(long id, CloseFeeStructureRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeBillingOptionsDto>> GetOptionsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FeeStudentOptionDto>>> SearchStudentsAsync(string search, int take = 20, CancellationToken cancellationToken = default);
    Task<ApiResponse<FeeStructureDto>> SaveFeeStructureAsync(long? feeStructureId, SaveFeeStructureRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InvoiceBatchPreflightDto>> PreviewInvoiceBatchAsync(GenerateStudentInvoiceBatchRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<InvoiceBatchResultDto>> GenerateInvoicesAsync(GenerateStudentInvoiceBatchRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentPaymentDto>> CollectPaymentAsync(CreateStudentPaymentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentInvoiceDto>> GetStudentInvoiceDocumentAsync(Guid studentReference, Guid documentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentPaymentDocumentDto>> GetStudentPaymentDocumentAsync(Guid studentReference, Guid documentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLedgerDto>> GetStudentLedgerAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<InvoiceAllocationAuditDto>> GetInvoiceAllocationAuditAsync(Guid studentReference, Guid invoiceReference, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentInvoiceDto>>> GetStudentInvoicesAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<StudentPaymentDto>>> GetStudentPaymentsAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
}
