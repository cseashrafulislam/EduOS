using EduOS.Core.DTOs.Transport;
using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportFleetInsightsTests
{
    [Fact]
    public void Summarizes_capacity_and_distinct_risk_vehicles_without_identifiers()
    {
        var rows = new[]
        {
            new TransportCapacityReportRowDto { Capacity = 20, ActiveRiders = 22, AvailableSeats = 0,
                IsActive = true, OverCapacity = true, CurrentRouteLinks = 0 },
            new TransportCapacityReportRowDto { Capacity = 10, ActiveRiders = 3, AvailableSeats = 7,
                IsActive = false, CurrentRouteLinks = 1 },
            new TransportCapacityReportRowDto { Capacity = 10, ActiveRiders = 9, AvailableSeats = 1,
                IsActive = true, CurrentRouteLinks = 1 }
        };
        var result = TransportService.SummarizeFleet(rows);
        Assert.Equal(3, result.TotalVehicles);
        Assert.Equal(2, result.ActiveVehicles);
        Assert.Equal(40, result.TotalSeats);
        Assert.Equal(34, result.ActiveRiders);
        Assert.Equal(8, result.AvailableSeats);
        Assert.Equal(1, result.OverCapacityVehicles);
        Assert.Equal(1, result.WithoutCurrentRouteVehicles);
        Assert.Equal(1, result.InactiveWithRidersVehicles);
        Assert.Equal(2, result.HighUtilizationVehicles);
        Assert.Equal(2, result.RiskVehicles);
        Assert.Equal(85m, result.FleetUtilizationPercent);
    }

    [Fact]
    public void Empty_fleet_returns_zero_without_division_error()
    {
        var result = TransportService.SummarizeFleet(Array.Empty<TransportCapacityReportRowDto>());
        Assert.Equal(0, result.TotalVehicles);
        Assert.Equal(0m, result.FleetUtilizationPercent);
    }

    [Fact]
    public void Aggregate_dto_does_not_expose_student_driver_or_vehicle_identity()
    {
        var properties = typeof(TransportFleetInsightsDto).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(properties, p => p.Contains("Name") || p.Contains("Student") ||
            p.Contains("Driver") || p.Contains("VehicleReference") || p.Contains("Fare"));
    }
}
