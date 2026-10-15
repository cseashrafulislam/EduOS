using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportVehicleDriverAssignmentContractTests
{
    [Fact]
    public void Vehicle_driver_workflow_uses_existing_entities_and_transactional_guards()
    {
        var service = Read("EduOS.Service", "Services", "Transport", "TransportService.VehicleDriverLinks.cs");
        Assert.Contains("_driverLinks.GetQueryable()", service);
        Assert.Contains("x.TenantId == tenant", service);
        Assert.Contains("CanManageDriverRegistry()", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("driver.LicenseExpiresOn", service);
        Assert.Contains("SequenceEqual(vehicleVersion)", service);
        Assert.Contains("SequenceEqual(driverVersion)", service);
        Assert.Contains("SequenceEqual(version)", service);
        Assert.Contains("x.EffectiveFrom <= end", service);
        Assert.Contains("x.IsCurrent && x.EffectiveTo.HasValue", service);
        Assert.Contains("DbUpdateConcurrencyException", service);
        Assert.Contains("link.UpdatedBy = _currentUser.UserId", service);
        Assert.Contains("Close or reschedule active riders", service);
        Assert.DoesNotContain("MonthlyFare =", service);
    }

    [Fact]
    public void Vehicle_driver_endpoints_and_ui_are_tenant_admin_only()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "TransportController.cs");
        var view = Read("EduOS.App", "Views", "TransportOperations", "Index.cshtml");
        var form = Read("EduOS.App", "Views", "TransportOperations", "_VehicleDriverLinks.cshtml");
        Assert.Contains("HttpGet(\"vehicle-driver-links\")", api);
        Assert.Contains("HttpPost(\"vehicle-driver-links\")", api);
        Assert.Contains("HttpPost(\"vehicle-driver-links/{id:long}/close\")", api);
        Assert.Contains("Authorize(Roles = \"TenantAdmin\")", api);
        Assert.Contains("User.IsInRole(\"TenantAdmin\")", view);
        Assert.Contains("<partial name=\"_VehicleDriverLinks\" />", view);
        Assert.Contains("transportVehicleDriverForm", form);
        Assert.Contains("AntiForgeryToken", form);
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
