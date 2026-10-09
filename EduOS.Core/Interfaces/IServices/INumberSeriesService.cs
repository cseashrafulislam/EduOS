using EduOS.Core.Common;
using EduOS.Core.DTOs.System;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-scoped numbering policy. The counter is server-managed, never writable through a setup DTO.</summary>
public interface INumberSeriesService
{
    Task<ApiResponse<PagedResult<NumberSeriesDto>>> GetSeriesAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<NumberSeriesDto>> SaveSeriesAsync(long? seriesId, SaveNumberSeriesRequestDto request, CancellationToken cancellationToken = default);
    /// <summary>Issue once per tenant, series key and client request ID; counter increment and idempotency record must commit atomically.</summary>
    Task<ApiResponse<string>> IssueNumberAsync(string seriesKey, Guid clientRequestId, CancellationToken cancellationToken = default);
}
