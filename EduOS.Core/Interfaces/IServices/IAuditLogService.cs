using EduOS.Core.Common;
using EduOS.Core.DTOs.System;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>
/// Privileged, tenant-scoped read-only audit reporting. Never expose arbitrary
/// audit deletion or unbounded in-memory exports as an application endpoint.
/// </summary>
public interface IAuditLogService
{
    Task<ApiResponse<PagedResult<AuditLogDto>>> SearchAsync(AuditLogFilterDto filter, CancellationToken cancellationToken = default);
    Task<ApiResponse<AuditLogStatisticsDto>> GetStatisticsAsync(AuditLogFilterDto filter, CancellationToken cancellationToken = default);
    /// <summary>Export at most 1,000 tenant-scoped matching entries; larger results require narrower filters.</summary>
    Task<ApiResponse<string>> ExportCsvAsync(AuditLogFilterDto filter, CancellationToken cancellationToken = default);
}
