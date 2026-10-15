using EduOS.Core.DTOs.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportCapacityReportContractTests
{
    [Fact]
    public void CapacityReportExposesAggregateCountsNotStudentOrDriverIdentity()
    {
        var names = typeof(TransportCapacityReportRowDto).GetProperties().Select(x => x.Name).ToArray();
        Assert.Contains(nameof(TransportCapacityReportRowDto.ActiveRiders), names);
        Assert.Contains(nameof(TransportCapacityReportRowDto.AvailableSeats), names);
        Assert.Contains(nameof(TransportCapacityReportRowDto.OverCapacity), names);
        Assert.DoesNotContain(names, x => x.Contains("Student", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Driver", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Fare", StringComparison.OrdinalIgnoreCase));
    }
}
