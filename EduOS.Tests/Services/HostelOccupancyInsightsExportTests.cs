using EduOS.Core.DTOs.Hostel;
using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelOccupancyInsightsExportTests
{
    [Fact]
    public void Csv_contains_every_occupancy_metric_and_invariant_utc_values()
    {
        var data = new HostelOccupancyInsightsDto
        {
            GeneratedAtUtc = new DateTime(2026, 10, 26, 1, 2, 3, DateTimeKind.Utc),
            TotalRooms = 10, ActiveRooms = 9, TotalCapacity = 100,
            ActiveBeds = 98, OccupiedBeds = 72, AvailableBeds = 26,
            OccupancyPercent = 72.5m, FullRooms = 2, OverCapacityRooms = 1,
            InactiveRoomsWithAllocations = 1, RoomsWithBedCapacityMismatch = 2,
            UnprovisionedRooms = 1, HighUtilizationRooms = 3, RiskRooms = 4
        };
        var csv = HostelService.BuildOccupancyInsightsCsv(data);
        Assert.StartsWith("Metric,Value\r\n", csv);
        Assert.Contains("Generated at UTC,2026-10-26T01:02:03.0000000Z\r\n", csv);
        Assert.Contains("Occupancy percent,72.5\r\n", csv);
        Assert.Contains("Rooms with bed capacity mismatch,2\r\n", csv);
        Assert.Contains("Rooms needing review,4\r\n", csv);
        Assert.Equal(16, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Csv_excludes_student_room_hostel_identity_and_financial_fields()
    {
        var csv = HostelService.BuildOccupancyInsightsCsv(new HostelOccupancyInsightsDto());
        Assert.DoesNotContain("Student", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Room number", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hostel name", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RoomId", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rent", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Deposit", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Payment", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_snapshot_is_exported_without_division_or_format_errors()
    {
        var csv = HostelService.BuildOccupancyInsightsCsv(new HostelOccupancyInsightsDto());
        Assert.Contains("Rooms,0\r\n", csv);
        Assert.Contains("Occupancy percent,0\r\n", csv);
    }

    [Fact]
    public void Null_snapshot_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => HostelService.BuildOccupancyInsightsCsv(null!));
    }
}
