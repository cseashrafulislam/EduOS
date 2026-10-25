using EduOS.Core.DTOs.Transport;
using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportFleetInsightsExportTests
{
    [Fact]
    public void Csv_contains_all_fleet_metrics_with_invariant_values_and_utc_timestamp()
    {
        var dto = new TransportFleetInsightsDto
        {
            GeneratedAtUtc = new DateTime(2026, 10, 26, 1, 2, 3, DateTimeKind.Utc),
            TotalVehicles = 3, ActiveVehicles = 2, TotalSeats = 40,
            ActiveRiders = 34, AvailableSeats = 8, FleetUtilizationPercent = 85.5m,
            OverCapacityVehicles = 1, WithoutCurrentRouteVehicles = 1,
            InactiveWithRidersVehicles = 1, HighUtilizationVehicles = 2, RiskVehicles = 2
        };
        var csv = TransportService.BuildFleetInsightsCsv(dto);
        Assert.StartsWith("Metric,Value\r\n", csv);
        Assert.Contains("Generated at UTC,2026-10-26T01:02:03.0000000Z\r\n", csv);
        Assert.Contains("Fleet utilization percent,85.5\r\n", csv);
        Assert.Contains("Vehicles needing review,2\r\n", csv);
        Assert.Equal(13, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Csv_contains_no_vehicle_student_driver_identity_or_financial_fields()
    {
        var csv = TransportService.BuildFleetInsightsCsv(new TransportFleetInsightsDto());
        Assert.DoesNotContain("Vehicle number", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Student", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Driver name", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("License", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fare", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Campus name", csv, StringComparison.OrdinalIgnoreCase);
    }
}
