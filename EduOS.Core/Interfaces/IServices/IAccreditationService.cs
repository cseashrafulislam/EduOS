using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;

namespace EduOS.Core.Interfaces.IServices;

public interface IAccreditationService
{
    Task<ApiResponse<List<ResearchCampusDto>>> CampusesAsync(CancellationToken ct = default);
    Task<ApiResponse<List<ResearchProgramDto>>> ProgramsAsync(long campusId, CancellationToken ct = default);
    Task<ApiResponse<List<ResearchReviewerDto>>> ReviewersAsync(long campusId, string? search, CancellationToken ct = default);
    Task<ApiResponse<AccreditationFrameworkDto>> CreateFrameworkAsync(AccreditationFrameworkCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<AccreditationFrameworkDto>> PublishFrameworkAsync(long frameworkId, AccreditationFrameworkPublishDto input, CancellationToken ct = default);
    Task<ApiResponse<AccreditationStandardDto>> AddStandardAsync(AccreditationStandardCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<List<AccreditationFrameworkDto>>> FrameworksAsync(CancellationToken ct = default);
    Task<ApiResponse<List<AccreditationStandardDto>>> StandardsAsync(long frameworkId, CancellationToken ct = default);
    Task<ApiResponse<AccreditationAssessmentDto>> CreateAsync(AccreditationAssessmentCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<AccreditationAssessmentDto>> CommandAsync(Guid reference, string action, AccreditationAssessmentCommandDto input, CancellationToken ct = default);
    Task<ApiResponse<AccreditationAssessmentDto>> DetailAsync(Guid reference, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AccreditationAssessmentDto>>> ListAsync(long? campusId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AccreditationAssessmentHistoryDto>>> HistoryAsync(Guid reference, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<List<AccreditationReportRowDto>>> ReportAsync(long campusId, DateTime from, DateTime to, CancellationToken ct = default);
}
