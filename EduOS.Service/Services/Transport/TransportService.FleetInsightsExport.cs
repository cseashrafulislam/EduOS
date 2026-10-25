using System.Globalization;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    // Reuse the authorized, bounded fleet projection. Export contains aggregates only.
    public async Task<ApiResponse<string>> ExportFleetInsightsCsvAsync(
        long? campusId, string? search, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetFleetInsightsAsync(campusId, search, cancellationToken);
        if (!snapshot.Success || snapshot.Data == null)
            return ApiResponse<string>.ErrorResponse(
                snapshot.Message ?? "Fleet insights unavailable.", snapshot.StatusCode);
        return ApiResponse<string>.SuccessResponse(BuildFleetInsightsCsv(snapshot.Data));
    }

    // Fixed metric names and invariant numeric values eliminate CSV formula injection
    // and prevent vehicle, student, driver, and financial data from leaking.
    public static string BuildFleetInsightsCsv(TransportFleetInsightsDto data)
    {
        ArgumentNullException.ThrowIfNull(data);
        static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        var metrics = new (string Name, string Value)[]
        {
            ("Generated at UTC", data.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("Vehicles", Number(data.TotalVehicles)),
            ("Active vehicles", Number(data.ActiveVehicles)),
            ("Seats", Number(data.TotalSeats)),
            ("Riders", Number(data.ActiveRiders)),
            ("Available seats", Number(data.AvailableSeats)),
            ("Fleet utilization percent", data.FleetUtilizationPercent.ToString(CultureInfo.InvariantCulture)),
            ("Over capacity vehicles", Number(data.OverCapacityVehicles)),
            ("Vehicles without current route", Number(data.WithoutCurrentRouteVehicles)),
            ("Inactive vehicles with riders", Number(data.InactiveWithRidersVehicles)),
            ("High utilization vehicles", Number(data.HighUtilizationVehicles)),
            ("Vehicles needing review", Number(data.RiskVehicles))
        };
        var csv = new StringBuilder("Metric,Value\r\n");
        foreach (var (name, value) in metrics)
            csv.Append(name).Append(',').Append(value).Append("\r\n");
        return csv.ToString();
    }
}
