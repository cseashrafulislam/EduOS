using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IFinanceAdjustmentService
{
    Task<ApiResponse<TransportBillingPolicyDto>> GetTransportBillingPolicyAsync(long campusId, CancellationToken ct = default);
    Task<ApiResponse<TransportBillingPolicyDto>> SaveTransportBillingPolicyAsync(TransportBillingPolicyDto request, CancellationToken ct = default);
    Task<ApiResponse<TransportClearanceDto>> RequestTransportClearanceAsync(RequestTransportClearanceDto request, CancellationToken ct = default);
    Task<ApiResponse<TransportClearanceDto>> ReviewTransportClearanceAsync(long id, ReviewTransportClearanceDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TransportClearanceDto>>> GetTransportClearancesAsync(long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<HostelBillingPolicyDto>> GetHostelBillingPolicyAsync(long campusId, CancellationToken ct = default);
    Task<ApiResponse<HostelBillingPolicyDto>> SaveHostelBillingPolicyAsync(HostelBillingPolicyDto request, CancellationToken ct = default);
    Task<ApiResponse<HostelClearanceDto>> RequestHostelClearanceAsync(RequestHostelClearanceDto request, CancellationToken ct = default);
    Task<ApiResponse<HostelClearanceDto>> ReviewHostelClearanceAsync(long id, ReviewHostelClearanceDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<HostelClearanceDto>>> GetHostelClearancesAsync(long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<DiscountAwardRegisterDto>> GetDiscountAwardRegisterAsync(long? campusId, long? ruleId, bool? active, string? search, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<HostelOccupiedTransferFinanceReviewDto>> ReviewHostelOccupiedTransferAsync(HostelOccupiedTransferFinanceReviewRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReservationFinanceChangeReviewDto>> ReviewReservationChangeAsync(ReservationFinanceChangeReviewRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReservationFinanceWorklistDto>> GetReservationFinanceWorklistAsync(ReservationFinanceWorklistQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReservationFinanceEvidenceDto>> GetReservationFinanceEvidenceAsync(ReservationFinanceEvidenceQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReservationCreditOptionsDto>> GetReservationCreditOptionsAsync(string kind,
        long clearanceId, string? invoiceSearch, string? accountSearch, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReservationFinancePreflightDto>> GetReservationFinancePreflightAsync(string kind, long id, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FinanceEnrollmentOptionDto>>> SearchEnrollmentsAsync(string search, CancellationToken cancellationToken = default);
    Task<ApiResponse<FinanceAdjustmentRegisterDto>> GetStudentRegisterAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<DiscountRuleDto>>> GetDiscountRulesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<DiscountRuleDto>> SaveDiscountRuleAsync(long? ruleId, SaveDiscountRuleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDiscountDto>> AssignDiscountAsync(AssignStudentDiscountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDiscountDto>> DeactivateStudentDiscountAsync(long discountId, DeactivateStudentDiscountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FineRuleDto>>> GetFineRulesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FineSourceInvoiceDto>>> GetFineSourceInvoicesAsync(Guid enrollmentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<FineRuleDto>> SaveFineRuleAsync(long? ruleId, SaveFineRuleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineRegisterDto>> GetFineRegisterAsync(StudentFineRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineDto>> ApplyFineAsync(ApplyStudentFineRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineDto>> WaiveFineAsync(long fineId, WaiveStudentFineRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PostedStudentFineInvoiceDto>> PostFineInvoiceAsync(long fineId, PostStudentFineInvoiceRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineReversalDto>> RequestFineReversalAsync(long fineId, RequestStudentFineReversalDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineReversalDto>> ReviewFineReversalAsync(long fineId, Guid reversalReference, ReviewStudentFineReversalDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundOperationsRegisterDto>> GetRefundRegisterAsync(long? campusId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> RequestRefundAsync(CreateRefundRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> ReviewRefundAsync(long refundId, ReviewRefundRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> RecordRefundDisbursementAsync(long refundId, RecordRefundDisbursementRequestDto request, CancellationToken cancellationToken = default);
}
