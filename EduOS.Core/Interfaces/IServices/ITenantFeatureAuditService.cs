using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface ITenantFeatureAuditService
{
    Task<ApiResponse<string>> ExportCsvAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TenantFeatureAuditRowDto>>> GetAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
