using EduOS.Core.Common;
using EduOS.Core.DTOs.Platform;

namespace EduOS.Core.Interfaces.IServices;

public interface ICentralInstitutionMisService
{
    Task<ApiResponse<CentralInstitutionMisDto>> GetAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>Same canonical metric rules, without querying unrelated modules.</summary>
    Task<ApiResponse<CentralInstitutionMisDto>> GetMetricAsync(
        string metricKey, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}
