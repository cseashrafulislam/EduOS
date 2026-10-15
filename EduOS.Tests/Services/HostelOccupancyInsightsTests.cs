using EduOS.Core.DTOs.Hostel;
using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelOccupancyInsightsTests
{
    [Fact]
    public void Summarizes_capacity_risks_without_double_counting_rooms()
    {
        var rows = new[]
        {
            new HostelCapacityRowDto { Capacity = 4, ActiveBeds = 4, OccupiedBeds = 4,
                AvailableBeds = 0, HostelActive = true, RoomActive = true },
            new HostelCapacityRowDto { Capacity = 2, ActiveBeds = 3, OccupiedBeds = 3,
                AvailableBeds = 0, HostelActive = true, RoomActive = true, HasRisk = true },
            new HostelCapacityRowDto { Capacity = 3, ActiveBeds = 3, OccupiedBeds = 1,
                AvailableBeds = 0, HostelActive = true, RoomActive = false, HasRisk = true },
            new HostelCapacityRowDto { Capacity = 2, ActiveBeds = 0, OccupiedBeds = 0,
                AvailableBeds = 0, HostelActive = true, RoomActive = true }
        };
        var result = HostelService.SummarizeOccupancy(rows);
        Assert.Equal(4, result.TotalRooms);
        Assert.Equal(3, result.ActiveRooms);
        Assert.Equal(11, result.TotalCapacity);
        Assert.Equal(10, result.ActiveBeds);
        Assert.Equal(8, result.OccupiedBeds);
        Assert.Equal(0, result.AvailableBeds);
        Assert.Equal(2, result.FullRooms);
        Assert.Equal(1, result.OverCapacityRooms);
        Assert.Equal(1, result.InactiveRoomsWithAllocations);
        Assert.Equal(1, result.RoomsWithBedCapacityMismatch);
        Assert.Equal(1, result.UnprovisionedRooms);
        Assert.Equal(2, result.HighUtilizationRooms);
        Assert.Equal(3, result.RiskRooms);
        Assert.Equal(72.7m, result.OccupancyPercent);
    }

    [Fact]
    public void Empty_capacity_returns_zero_without_division_error()
    {
        var result = HostelService.SummarizeOccupancy(Array.Empty<HostelCapacityRowDto>());
        Assert.Equal(0, result.TotalRooms);
        Assert.Equal(0m, result.OccupancyPercent);
    }

    [Fact]
    public void Insights_dto_exposes_no_student_hostel_room_or_financial_identity()
    {
        var names = typeof(HostelOccupancyInsightsDto).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(names, p => p.Contains("Student") || p.Contains("Name") ||
            p.Contains("RoomId") || p.Contains("HostelId") || p.Contains("Rent") ||
            p.Contains("Deposit") || p.Contains("Payment"));
    }
}
