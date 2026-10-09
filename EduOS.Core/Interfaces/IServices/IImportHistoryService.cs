using EduOS.Core.Common;
using EduOS.Core.DTOs.System;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped import audit/history. Processing belongs to the owning module, not this read contract.</summary>
public interface IImportHistoryService
{
    Task<ApiResponse<PagedResult<ImportLogDto>>> GetImportsAsync(string? importType, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<ImportLogDto>> GetImportAsync(Guid importReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<ImportLogItemDto>>> GetImportRowsAsync(Guid importReference, int page, int pageSize, CancellationToken cancellationToken = default);
}
