using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportDriverRegistryContractTests
{
    [Fact]
    public void Driver_registry_uses_canonical_entities_and_tenant_only_administration()
    {
        var service = Read("EduOS.Service", "Services", "Transport", "TransportService.DriverRegistry.cs");
        var repository = Read("EduOS.Service", "Services", "Transport", "TransportService.DriverRepositories.cs");
        Assert.Contains("_drivers.GetQueryable()", service);
        Assert.Contains("x.TenantId == tenant && !x.IsDeleted", service);
        Assert.Contains("_currentUser.IsTenantAdmin", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("x.LicenseNumber.ToUpper() == license", service);
        Assert.Contains("x.IsCurrent && (!x.EffectiveTo.HasValue", service);
        Assert.Contains("existing.RowVersion.AsSpan().SequenceEqual(version)", service);
        Assert.Contains("DbUpdateConcurrencyException", service);
        Assert.Contains("existing.UpdatedBy = _currentUser.UserId", service);
        Assert.Contains("IGenericRepository<TransportDriver>", repository);
        Assert.Contains("IGenericRepository<VehicleDriverAssignment>", repository);
        Assert.DoesNotContain("CampusId =", service);
        Assert.DoesNotContain("MonthlyFare =", service);
    }

    [Fact]
    public void Driver_registry_has_authorized_api_and_tenant_admin_only_ui()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "TransportController.cs");
        var view = Read("EduOS.App", "Views", "TransportOperations", "Index.cshtml");
        var partial = Read("EduOS.App", "Views", "TransportOperations", "_DriverRegistry.cshtml");
        var script = Read("EduOS.App", "wwwroot", "js", "transport-operations", "driver-registry.js");
        Assert.Contains("HttpGet(\"driver-registry\")", api);
        Assert.Contains("HttpPost(\"driver-registry\")", api);
        Assert.Contains("HttpPut(\"driver-registry/{driverId:long}\")", api);
        Assert.Contains("Authorize(Roles = \"TenantAdmin\")", api);
        Assert.Contains("User.IsInRole(\"TenantAdmin\")", view);
        Assert.Contains("<partial name=\"_DriverRegistry\" />", view);
        Assert.Contains("driver-registry.js", view);
        Assert.Contains("transportDriverRegistryForm", partial);
        Assert.Contains("RequestVerificationToken", script);
        Assert.Contains("textContent", script);
        Assert.Contains("rowVersion: editing?.rowVersion", script);
    }

    [Fact]
    public void Driver_license_changes_preserve_current_and_future_assignments()
    {
        var service = Read("EduOS.Service", "Services", "Transport", "TransportService.DriverRegistry.cs");
        Assert.Contains("existing.LicenseExpiresOn != request.LicenseExpiresOn", service);
        Assert.Contains("x.TenantId == tenant && !x.IsDeleted && x.TransportDriverId == existing.Id", service);
        Assert.Contains("x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today)", service);
        Assert.Contains("!requestedExpiry.HasValue || !x.EffectiveTo.HasValue", service);
        Assert.Contains("x.EffectiveTo.Value > requestedExpiry.Value", service);
        Assert.Contains("License expiry cannot precede a current vehicle assignment end date", service);
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
