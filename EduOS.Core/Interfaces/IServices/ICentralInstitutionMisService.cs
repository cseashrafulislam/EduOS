using EduOS.Core.Common;
using EduOS.Core.DTOs.Platform;

namespace EduOS.Core.Interfaces.IServices;

public interface ICentralInstitutionMisService
{
    Task<ApiResponse<CentralInstitutionMisDto>> GetAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);
}
