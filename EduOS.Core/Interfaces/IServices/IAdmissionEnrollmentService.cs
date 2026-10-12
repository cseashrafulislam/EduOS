using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;

namespace EduOS.Core.Interfaces.IServices;

public interface IAdmissionEnrollmentService
{
    Task<ApiResponse<AdmissionEnrollmentOptionsDto>> GetOptionsAsync(Guid applicationReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionDecisionDto?>> GetOfferAsync(Guid applicationReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionDecisionDto>> IssueOfferAsync(Guid applicationReference, MakeAdmissionDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionDecisionDto>> RecordVerifiedAcceptanceAsync(Guid applicationReference, VerifyAdmissionOfferAcceptanceRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmittedStudentDto>> AdmitAsync(Guid applicationReference, AdmitAdmissionApplicationDto request, CancellationToken cancellationToken = default);
}
