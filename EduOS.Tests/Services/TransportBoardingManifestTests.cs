using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportBoardingManifestTests
{
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
