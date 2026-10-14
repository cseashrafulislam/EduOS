using Xunit;

namespace EduOS.Tests.Services;

public class TransportVehicleSetupContractTests
{
    [Fact]
    public void Vehicle_setup_reuses_canonical_vehicle_and_enforces_capacity_and_campus()
    {
        var source = Read("EduOS.Service", "Services", "Transport", "TransportService.VehicleSetup.cs");
        Assert.Contains("_vehicles.GetQueryable()", source);
        Assert.Contains("CanManageCampusAsync", source);
        Assert.Contains("AllowedCampusIds(tenant)", source);
        Assert.Contains("IsolationLevel.Serializable", source);
        Assert.Contains("x.State == TransportAssignmentState.Active", source);
        Assert.Contains("request.Capacity < occupied", source);
        Assert.Contains("occupied > 0 || currentRoutes > 0", source);
        Assert.Contains("vehicle.RowVersion.AsSpan().SequenceEqual(version)", source);
        Assert.Contains("DbUpdateConcurrencyException", source);
        Assert.Contains("vehicle.UpdatedBy = _currentUser.UserId", source);
        Assert.DoesNotContain("DefaultFare =", source);
        Assert.DoesNotContain("MonthlyFare =", source);
    }

    [Fact]
    public void Vehicle_setup_has_authorized_endpoints_and_integrated_view()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "TransportController.cs");
        var view = Read("EduOS.App", "Views", "TransportOperations", "Index.cshtml");
        var script = Read("EduOS.App", "wwwroot", "js", "transport-operations", "vehicle-setup.js");
        Assert.Contains("HttpGet(\"vehicle-setup\")", api);
        Assert.Contains("HttpPost(\"vehicle-setup\")", api);
        Assert.Contains("HttpPut(\"vehicle-setup/{reference:guid}\")", api);
        Assert.Contains("TenantAdmin,Principal,TransportManager", api);
        Assert.Contains("transportVehicleSetupForm", view);
        Assert.Contains("vehicle-setup.js", view);
        Assert.Contains("RequestVerificationToken", script);
        Assert.Contains("textContent", script);
        Assert.Contains("rowVersion: item.rowVersion", script);
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
