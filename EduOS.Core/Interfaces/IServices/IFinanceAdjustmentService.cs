using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;

namespace EduOS.Core.Interfaces.IServices;

public interface IFinanceAdjustmentService
{
    Task<ApiResponse<IReadOnlyList<DiscountRuleDto>>> GetDiscountRulesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<DiscountRuleDto>> SaveDiscountRuleAsync(long? ruleId, SaveDiscountRuleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDiscountDto>> AssignDiscountAsync(AssignStudentDiscountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<FineRuleDto>>> GetFineRulesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<FineRuleDto>> SaveFineRuleAsync(long? ruleId, SaveFineRuleRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineDto>> ApplyFineAsync(ApplyStudentFineRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentFineDto>> WaiveFineAsync(long fineId, WaiveStudentFineRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> RequestRefundAsync(CreateRefundRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> ReviewRefundAsync(long refundId, ReviewRefundRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<RefundDto>> RecordRefundDisbursementAsync(long refundId, RecordRefundDisbursementRequestDto request, CancellationToken cancellationToken = default);
}
