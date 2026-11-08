using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;

namespace EduOS.Core.Interfaces.IServices;

public interface IObeAttainmentService
{
    Task<ApiResponse<List<ObeCurriculumCourseDto>>> CoursesAsync(long campusId, long programId, CancellationToken ct = default);
    Task<ApiResponse<List<ObePublishedBatchDto>>> PublicationsAsync(long campusId, long programId, CancellationToken ct = default);
    Task<ApiResponse<List<ObePolicyDto>>> PoliciesAsync(long campusId, long programId, CancellationToken ct = default);
    Task<ApiResponse<ObePolicyDetailDto>> PolicyAsync(long policyId, CancellationToken ct = default);
    Task<ApiResponse<ObePolicyDetailDto>> CreatePolicyAsync(ObePolicyCreateDto input, CancellationToken ct = default);
    Task<ApiResponse<ObePreviewDto>> PreviewAsync(long policyId, long publicationId, CancellationToken ct = default);
    Task<ApiResponse<ObeReportDto>> SubmitAsync(long policyId, ObeSubmitDto input, CancellationToken ct = default);
    Task<ApiResponse<ObeReportDto>> ReviewAsync(Guid reference, ObeReviewDto input, CancellationToken ct = default);
    Task<ApiResponse<List<ObeReportDto>>> ReportsAsync(long campusId, long programId, CancellationToken ct = default);
    Task<ApiResponse<ObeReportDto>> ReportAsync(Guid reference, CancellationToken ct = default);
}
