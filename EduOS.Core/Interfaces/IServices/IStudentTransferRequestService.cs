using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentTransferRequestService
{
    Task<ApiResponse<StudentTransferEligibilityDto>> GetEligibilityAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<TransferRequestDto>>> GetRequestsAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<TransferRequestDto>> SubmitAsync(SubmitStudentTransferRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<TransferRequestDto>> ReviewAsync(Guid transferReference, ReviewTransferRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<DestinationTransferInboxDto>>> GetDestinationInboxAsync(CancellationToken ct = default);
    Task<ApiResponse<DestinationTransferInboxDto>> DecideDestinationAsync(Guid transferReference, DestinationTransferDecisionDto request, CancellationToken ct = default);
    Task<ApiResponse<DestinationTransferInboxDto>> LinkDestinationEnrollmentAsync(Guid transferReference, DestinationTransferEnrollmentDto request, CancellationToken ct = default);
    Task<ApiResponse<TransferRequestDto>> CompleteAsync(Guid transferReference, CompleteTransferRequestDto request, CancellationToken ct = default);
}
