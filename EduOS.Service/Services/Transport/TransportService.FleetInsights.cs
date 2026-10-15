using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    // Uses the same tenant/campus-authorized, privacy-safe projection as the existing report.
    // The 1,000-vehicle bound avoids unbounded fan-out; this is a live, not transactional, snapshot.
    public async Task<ApiResponse<TransportFleetInsightsDto>> GetFleetInsightsAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default)
    {
        var first = await GetCapacityReportAsync(campusId, 1, 100, search, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<TransportFleetInsightsDto>.ErrorResponse(
                first.Message ?? "Fleet insights unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<TransportFleetInsightsDto>.ErrorResponse(
                "Fleet exceeds 1,000 vehicles. Filter by campus or search.", 413);
        var rows = new List<TransportCapacityReportRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetCapacityReportAsync(campusId, page, 100, search, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<TransportFleetInsightsDto>.ErrorResponse(
                    "Fleet changed during retrieval. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !ids.Add(x.VehicleReference)))
            return ApiResponse<TransportFleetInsightsDto>.ErrorResponse(
                "Fleet changed during retrieval. Retry.", 409);
        return ApiResponse<TransportFleetInsightsDto>.SuccessResponse(SummarizeFleet(rows));
    }

    // Pure projection for executable unit tests. No names, IDs, student or driver data escape.
    public static TransportFleetInsightsDto SummarizeFleet(IReadOnlyList<TransportCapacityReportRowDto> rows)
    {
        var seats = rows.Sum(x => (long)Math.Max(0, x.Capacity));
        var riders = rows.Sum(x => (long)Math.Max(0, x.ActiveRiders));
        return new TransportFleetInsightsDto
        {
            GeneratedAtUtc = DateTime.UtcNow,
            TotalVehicles = rows.Count,
            ActiveVehicles = rows.Count(x => x.IsActive),
            TotalSeats = seats,
            ActiveRiders = riders,
            AvailableSeats = rows.Sum(x => (long)Math.Max(0, x.AvailableSeats)),
            OverCapacityVehicles = rows.Count(x => x.OverCapacity),
            WithoutCurrentRouteVehicles = rows.Count(x => x.IsActive && x.CurrentRouteLinks == 0),
            InactiveWithRidersVehicles = rows.Count(x => !x.IsActive && x.ActiveRiders > 0),
            HighUtilizationVehicles = rows.Count(x => x.IsActive && x.Capacity > 0 && x.ActiveRiders * 10L >= x.Capacity * 9L),
            RiskVehicles = rows.Count(x => x.OverCapacity || (x.IsActive && x.CurrentRouteLinks == 0) ||
                (!x.IsActive && x.ActiveRiders > 0)),
            FleetUtilizationPercent = seats > 0
                ? Math.Round(100m * riders / seats, 1, MidpointRounding.AwayFromZero) : 0m
        };
    }
}
