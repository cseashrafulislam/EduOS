using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>
    /// Actionable room risk worklist built from the canonical capacity projection.
    /// Filter BEFORE final pagination so TotalCount and CSV describe the same risk cohort.
    /// Bounded to 1,000 candidate rooms to avoid unbounded in-memory scans; callers
    /// can narrow by campus/search. No student or financial fields are returned.
    /// </summary>
    private async Task<ApiResponse<PagedResult<HostelCapacityRowDto>>> GetCapacityRiskAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        var first = await GetCapacityAsync(campusId, 1, 100, search, cancellationToken, false);
        if (!first.Success || first.Data == null)
            return ApiResponse<PagedResult<HostelCapacityRowDto>>.ErrorResponse(
                first.Message ?? "Room risk report unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<PagedResult<HostelCapacityRowDto>>.ErrorResponse(
                "Risk worklist exceeds 1,000 candidate rooms. Narrow by campus or search.", 413);

        var all = new List<HostelCapacityRowDto>(first.Data.Items);
        for (var nextPage = 2; nextPage <= (first.Data.TotalCount + 99) / 100; nextPage++)
        {
            var next = await GetCapacityAsync(campusId, nextPage, 100, search, cancellationToken, false);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<PagedResult<HostelCapacityRowDto>>.ErrorResponse(
                    "Room capacity changed during retrieval. Refresh and retry.", 409);
            all.AddRange(next.Data.Items);
        }
        var seen = new HashSet<long>();
        if (all.Count != first.Data.TotalCount || all.Any(x => !seen.Add(x.RoomId)))
            return ApiResponse<PagedResult<HostelCapacityRowDto>>.ErrorResponse(
                "Room capacity changed during retrieval. Refresh and retry.", 409);
        var risks = all.Where(x => x.HasRisk).ToList();
        return ApiResponse<PagedResult<HostelCapacityRowDto>>.SuccessResponse(new PagedResult<HostelCapacityRowDto>
        {
            Items = risks.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = risks.Count, Page = page, PageSize = pageSize
        });
    }
}
