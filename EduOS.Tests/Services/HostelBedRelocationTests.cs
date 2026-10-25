using EduOS.Service.Services.Hostel;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelBedRelocationTests
{
    [Theory]
    [InlineData(true, false, true, true, 1, 2, false, true)]
    [InlineData(true, true, true, true, 1, 2, false, false)]
    [InlineData(false, false, true, true, 1, 2, false, false)]
    [InlineData(true, false, false, true, 1, 2, false, false)]
    [InlineData(true, false, true, false, 1, 2, false, false)]
    [InlineData(true, false, true, true, 2, 2, false, false)]
    [InlineData(true, false, true, true, 1, 2, true, false)]
    [InlineData(true, false, true, true, 0, 0, false, false)]
    public void Physical_bed_move_requires_no_history_and_spare_capacity(
        bool active, bool hasHistory, bool sameHostel, bool destinationActive,
        int physicalBeds, int capacity, bool duplicate, bool expected)
    {
        Assert.Equal(expected, HostelService.CanRelocateUnusedBed(
            active, hasHistory, sameHostel, destinationActive, physicalBeds, capacity, duplicate));
    }
}
