using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>
    /// Filter capacity exceptions before final pagination. Bounded to 1,000 vehicles so
    /// a cross-campus scan cannot cause unbounded reads or silently omit risk rows.
    /// </summary>
    private async Task<ApiResponse<PagedResult<TransportCapacityReportRowDto>>> GetCapacityRiskAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var first = await GetCapacityReportAsync(campusId, 1, 100, search, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<PagedResult<TransportCapacityReportRowDto>>.ErrorResponse(
                first.Message ?? "Capacity risk report unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<PagedResult<TransportCapacityReportRowDto>>.ErrorResponse(
                "Risk filter exceeds 1,000 candidate vehicles. Narrow campus or search.", 413);
        var all = new List<TransportCapacityReportRowDto>(first.Data.Items);
        for (var scanPage = 2; scanPage <= (first.Data.TotalCount + 99) / 100; scanPage++)
        {
            var next = await GetCapacityReportAsync(campusId, scanPage, 100, search, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<PagedResult<TransportCapacityReportRowDto>>.ErrorResponse(
                    "Capacity report changed during risk scan. Retry.", 409);
            all.AddRange(next.Data.Items);
        }
        var unique = new HashSet<Guid>();
        if (all.Count != first.Data.TotalCount || all.Any(x => !unique.Add(x.VehicleReference)))
            return ApiResponse<PagedResult<TransportCapacityReportRowDto>>.ErrorResponse(
                "Capacity report changed during risk scan. Retry.", 409);
        var risks = all.Where(x => x.HasRisk).ToList();
        return ApiResponse<PagedResult<TransportCapacityReportRowDto>>.SuccessResponse(
            new PagedResult<TransportCapacityReportRowDto>
            {
                Items = risks.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = risks.Count, Page = page, PageSize = pageSize
            });
    }
}
