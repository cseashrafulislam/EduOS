using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportAssignmentClosureTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact] public void Same_day_closure_is_valid() =>
        Assert.True(TransportService.IsValidTransportClosureDate(Today, Today, Today));
    [Fact] public void Historical_closure_is_valid() =>
        Assert.True(TransportService.IsValidTransportClosureDate(Today.AddDays(-5), Today.AddDays(-2), Today));
    [Fact] public void Future_closure_is_rejected() =>
        Assert.False(TransportService.IsValidTransportClosureDate(Today.AddDays(-5), Today.AddDays(1), Today));
    [Fact] public void Upcoming_assignment_requires_cancellation() =>
        Assert.False(TransportService.IsValidTransportClosureDate(Today.AddDays(1), Today.AddDays(1), Today));
    [Fact] public void Closure_before_start_is_rejected() =>
        Assert.False(TransportService.IsValidTransportClosureDate(Today.AddDays(-5), Today.AddDays(-6), Today));
}
