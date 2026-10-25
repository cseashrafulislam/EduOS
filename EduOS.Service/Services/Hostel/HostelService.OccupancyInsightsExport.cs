using System.Globalization;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    // The existing tenant/campus-authorized, bounded projection remains the only source.
    // The export deliberately contains no hostel, room, student or payment identifiers.
    public async Task<ApiResponse<string>> ExportOccupancyInsightsCsvAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetOccupancyInsightsAsync(campusId, search, cancellationToken);
        if (!snapshot.Success || snapshot.Data == null)
            return ApiResponse<string>.ErrorResponse(
                snapshot.Message ?? "Hostel occupancy insights unavailable.", snapshot.StatusCode);
        return ApiResponse<string>.SuccessResponse(BuildOccupancyInsightsCsv(snapshot.Data));
    }

    // All labels are constants and all values are numeric or UTC, avoiding CSV formula injection.
    public static string BuildOccupancyInsightsCsv(HostelOccupancyInsightsDto data)
    {
        ArgumentNullException.ThrowIfNull(data);
        static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        var metrics = new (string Name, string Value)[]
        {
            ("Generated at UTC", data.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("Rooms", Number(data.TotalRooms)),
            ("Active rooms", Number(data.ActiveRooms)),
            ("Configured capacity", Number(data.TotalCapacity)),
            ("Active beds", Number(data.ActiveBeds)),
            ("Occupied beds", Number(data.OccupiedBeds)),
            ("Available beds", Number(data.AvailableBeds)),
            ("Occupancy percent", data.OccupancyPercent.ToString(CultureInfo.InvariantCulture)),
            ("Full rooms", Number(data.FullRooms)),
            ("Over capacity rooms", Number(data.OverCapacityRooms)),
            ("Inactive rooms with allocations", Number(data.InactiveRoomsWithAllocations)),
            ("Rooms with bed capacity mismatch", Number(data.RoomsWithBedCapacityMismatch)),
            ("Unprovisioned rooms", Number(data.UnprovisionedRooms)),
            ("High utilization rooms", Number(data.HighUtilizationRooms)),
            ("Rooms needing review", Number(data.RiskRooms))
        };
        var csv = new StringBuilder("Metric,Value\r\n");
        foreach (var (name, value) in metrics)
            csv.Append(name).Append(',').Append(value).Append("\r\n");
        return csv.ToString();
    }
}
