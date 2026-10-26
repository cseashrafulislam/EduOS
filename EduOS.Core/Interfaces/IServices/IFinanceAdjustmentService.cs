using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IFinanceAdjustmentService
{
    Task<ApiResponse<ReservationFinanceWorklistDto>> GetReservationFinanceWorklistAsync(ReservationFinanceWorklistQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ReservationFinancePreflightDto>> GetReservationFinancePreflightAsync(string kind, long id, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FinanceEnrollmentOptionDto>>> SearchEnrollmentsAsync(string search, CancellationToken cancellationToken = default);
    Task<ApiResponse<FinanceAdjustmentRegisterDto>> GetStudentRegisterAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<DiscountRuleDto>>> GetDiscountRulesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<DiscountRuleDto>> SaveDiscountRuleAsync(long? ruleId, SaveDiscountRuleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDiscountDto>> AssignDiscountAsync(AssignStudentDiscountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDiscountDto>> DeactivateStudentDiscountAsync(long discountId, DeactivateStudentDiscountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FineRuleDto>>> GetFineRulesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<FineRuleDto>> SaveFineRuleAsync(long? ruleId, SaveFineRuleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineRegisterDto>> GetFineRegisterAsync(StudentFineRegisterQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineDto>> ApplyFineAsync(ApplyStudentFineRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineDto>> WaiveFineAsync(long fineId, WaiveStudentFineRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundOperationsRegisterDto>> GetRefundRegisterAsync(long? campusId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> RequestRefundAsync(CreateRefundRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> ReviewRefundAsync(long refundId, ReviewRefundRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> RecordRefundDisbursementAsync(long refundId, RecordRefundDisbursementRequestDto request, CancellationToken cancellationToken = default);
}
