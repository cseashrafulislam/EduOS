using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;

namespace EduOS.Core.Interfaces.IServices;

public interface IResearchGrantOutputService
{
    Task<ApiResponse<ResearchGrantOutputDto>> SubmitAsync(Guid grantReference,
        ResearchGrantOutputSubmitDto input, CancellationToken ct = default);
    Task<ApiResponse<ResearchGrantOutputDto>> ReviewAsync(Guid grantReference, Guid outputReference,
        ResearchGrantOutputReviewDto input, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ResearchGrantOutputDto>>> ListAsync(Guid grantReference,
        int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<ApiResponse<List<ResearchGrantOutcomeReportDto>>> ReportAsync(long campusId,
        DateTime from, DateTime to, CancellationToken ct = default);
}
