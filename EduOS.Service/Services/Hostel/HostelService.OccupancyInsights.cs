using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    // Reuses the existing tenant/campus-scoped capacity projection; no second occupancy source.
    // Bounded live pagination detects changing counts/duplicate room IDs but is not a SQL snapshot.
    public async Task<ApiResponse<HostelOccupancyInsightsDto>> GetOccupancyInsightsAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default)
    {
        var first = await GetCapacityAsync(campusId, 1, 100, search, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<HostelOccupancyInsightsDto>.ErrorResponse(
                first.Message ?? "Hostel occupancy insights unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<HostelOccupancyInsightsDto>.ErrorResponse(
                "Occupancy exceeds 1,000 rooms. Filter by campus or search.", 413);
        var rows = new List<HostelCapacityRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetCapacityAsync(campusId, page, 100, search, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<HostelOccupancyInsightsDto>.ErrorResponse(
                    "Occupancy changed during retrieval. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !ids.Add(x.RoomId)))
            return ApiResponse<HostelOccupancyInsightsDto>.ErrorResponse(
                "Occupancy changed during retrieval. Retry.", 409);
        return ApiResponse<HostelOccupancyInsightsDto>.SuccessResponse(SummarizeOccupancy(rows));
    }

    // Pure projection for unit tests. Room, student, hostel, and financial identifiers never escape.
    public static HostelOccupancyInsightsDto SummarizeOccupancy(IReadOnlyList<HostelCapacityRowDto> rows)
    {
        var capacity = rows.Sum(x => (long)Math.Max(0, x.Capacity));
        var occupied = rows.Sum(x => (long)Math.Max(0, x.OccupiedBeds));
        return new HostelOccupancyInsightsDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TotalRooms = rows.Count,
            ActiveRooms = rows.Count(x => x.HostelActive && x.RoomActive),
            TotalCapacity = capacity,
            ActiveBeds = rows.Sum(x => (long)Math.Max(0, x.ActiveBeds)),
            OccupiedBeds = occupied,
            AvailableBeds = rows.Sum(x => (long)Math.Max(0, x.AvailableBeds)),
            FullRooms = rows.Count(x => x.HostelActive && x.RoomActive &&
                x.Capacity > 0 && x.ActiveBeds > 0 && x.AvailableBeds == 0),
            OverCapacityRooms = rows.Count(x => x.OccupiedBeds > x.Capacity),
            InactiveRoomsWithAllocations = rows.Count(x => (!x.HostelActive || !x.RoomActive) && x.OccupiedBeds > 0),
            RoomsWithBedCapacityMismatch = rows.Count(x => x.ActiveBeds > x.Capacity),
            UnprovisionedRooms = rows.Count(x => x.HostelActive && x.RoomActive &&
                x.Capacity > 0 && x.ActiveBeds == 0),
            HighUtilizationRooms = rows.Count(x => x.HostelActive && x.RoomActive &&
                x.Capacity > 0 && x.OccupiedBeds * 10L >= x.Capacity * 9L),
            RiskRooms = rows.Count(x => x.HasRisk ||
                (x.HostelActive && x.RoomActive && x.Capacity > 0 && x.ActiveBeds == 0)),
            OccupancyPercent = capacity > 0
                ? Math.Round(100m * occupied / capacity, 1, MidpointRounding.AwayFromZero) : 0m
        };
    }
}
