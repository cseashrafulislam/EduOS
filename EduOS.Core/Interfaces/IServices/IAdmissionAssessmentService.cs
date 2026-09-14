using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;

namespace EduOS.Core.Interfaces.IServices;

public interface IAdmissionAssessmentService
{
    Task<ApiResponse<IReadOnlyList<AdmissionTestDto>>> GetTestsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionTestDto>> CreateTestAsync(SaveAdmissionTestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionTestDto>> UpdateTestAsync(long id, SaveAdmissionTestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<AdmissionResultDto>>> SaveResultsAsync(long testId, SaveAdmissionResultsDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionMeritListDto>> GetMeritListAsync(long testId, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionMeritListDto>> PublishMeritListAsync(long testId, CancellationToken cancellationToken = default);
}
