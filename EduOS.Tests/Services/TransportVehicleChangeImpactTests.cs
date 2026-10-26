using EduOS.Service.Services.Transport;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportVehicleChangeImpactTests
{
    [Theory]
    [InlineData(2, true, 2, 3, 1, 1, true)]
    [InlineData(1, true, 2, 3, 1, 1, false)]
    [InlineData(1, true, 1, 0, 0, 0, true)]
    [InlineData(1, false, 0, 0, 0, 0, true)]
    [InlineData(1, false, 0, 1, 0, 0, false)]
    [InlineData(1, false, 0, 0, 1, 0, false)]
    [InlineData(1, false, 0, 0, 0, 1, false)]
    [InlineData(0, true, 0, 0, 0, 0, false)]
    [InlineData(1001, true, 0, 0, 0, 0, false)]
    public void Preflight_fails_closed_for_peak_capacity_and_deactivation_conflicts(
        int capacity, bool active, int peak, int reservations, int routes, int drivers, bool expected)
    {
        Assert.Equal(expected, TransportService.CanApplyVehicleSettings(
            capacity, active, peak, reservations, routes, drivers));
    }

    [Fact]
    public void Preview_and_save_use_canonical_tenant_campus_and_concurrency_guards()
    {
        var service = Read("EduOS.Service", "Services", "Transport", "TransportService.VehicleChangeImpact.cs");
        var write = Read("EduOS.Service", "Services", "Transport", "TransportService.VehicleSetup.cs");
        var api = Read("EduOS.App", "Controllers", "Api", "TransportController.cs");
        var view = Read("EduOS.App", "Views", "TransportOperations", "_VehicleSetup.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "transport-operations", "vehicle-setup.js");
        Assert.Contains("CanManageCampusAsync", service);
        Assert.Contains("x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id", service);
        Assert.Contains("vehicle.RowVersion.AsSpan().SequenceEqual(version)", service);
        Assert.Contains("PeakReservedSeats(", service);
        Assert.Contains("DateOnly.MaxValue", service);
        Assert.Contains("Take(10001)", service);
        Assert.Contains("request.Capacity < peak", write);
        Assert.Contains("IsolationLevel.Serializable", write);
        Assert.Contains("HttpGet(\"vehicle-setup/{reference:guid}/change-impact\")", api);
        Assert.Contains("Authorize(Roles = \"TenantAdmin,Principal,TransportManager\")", api);
        Assert.Contains("transportVehicleSetupRows", view);
        Assert.Contains("vehicle-setup.js", Read("EduOS.App", "Views", "TransportOperations", "Index.cshtml"));
        Assert.Contains("RequestVerificationToken", js);
        Assert.Contains("impact.textContent", js);
        Assert.DoesNotContain("impact.innerHTML", js);
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Missing repository source: " + Path.Combine(segments));
    }
}
