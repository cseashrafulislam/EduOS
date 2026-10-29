using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportBoardingManifestTests
{
    [Theory]
    [InlineData("Ready", 2, 1, 1, 1, true, "OverCapacity")]
    [InlineData("Ready", 1, 2, 2, 1, true, "RouteConflict")]
    [InlineData("Ready", 1, 2, 1, 0, false, "NoDriver")]
    [InlineData("Ready", 1, 2, 1, 2, true, "DriverConflict")]
    [InlineData("Ready", 1, 2, 1, 1, false, "DriverReview")]
    [InlineData("Ready", 1, 2, 1, 1, true, "Ready")]
    [InlineData("PickupReview", 1, 2, 1, 1, true, "PickupReview")]
    public void Dispatch_readiness_rejects_unsafe_vehicle_and_driver_coverage(
        string baseReadiness, int riders, int capacity, int routes, int drivers,
        bool eligible, string expected)
    {
        Assert.Equal(expected, TransportService.ClassifyBoardingDispatch(
            baseReadiness, riders, capacity, routes, drivers, eligible));
    }

    [Theory]
    [InlineData(0, "Ready")]
    [InlineData(1, "VehicleCaseReview")]
    [InlineData(3, "VehicleCaseReview")]
    public void Unresolved_vehicle_cases_block_ready_planning_roster(int openCases, string expected)
    {
        Assert.Equal(expected, TransportService.ClassifyBoardingDispatch(
            "Ready", 1, 30, 1, 1, true, openCases));
    }

    [Fact]
    public void Former_campus_hold_uses_the_existing_fail_closed_vehicle_case_classification()
    {
        Assert.Equal("VehicleCaseReview", TransportService.ClassifyBoardingDispatch(
            "Ready", 1, 30, 1, 1, true, 1));
    }

    [Fact]
    public void Other_readiness_failures_are_not_masked_by_vehicle_case_hold()
    {
        Assert.Equal("OverCapacity", TransportService.ClassifyBoardingDispatch(
            "Ready", 31, 30, 1, 1, true, 2));
        Assert.Equal("PickupReview", TransportService.ClassifyBoardingDispatch(
            "PickupReview", 1, 30, 1, 1, true, 2));
    }

    [Theory]
    [InlineData(false, true, true, true, true, true, "EnrollmentReview")]
    [InlineData(true, false, true, true, true, true, "RouteReview")]
    [InlineData(true, true, false, true, true, true, "VehicleReview")]
    [InlineData(true, true, true, true, true, false, "MissingRouteVehicleLink")]
    [InlineData(true, true, true, false, true, true, "PickupReview")]
    [InlineData(true, true, true, true, false, true, "DropReview")]
    [InlineData(true, true, true, true, true, true, "Ready")]
    public void Roster_classification_is_fail_closed(bool enrollment, bool route,
        bool vehicle, bool pickup, bool drop, bool link, string expected)
    {
        Assert.Equal(expected, TransportService.ClassifyBoardingManifest(
            enrollment, route, vehicle, pickup, drop, link));
    }
}
