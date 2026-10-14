using Xunit;
namespace EduOS.Tests.Services;
public class TransportRouteVehicleLinkContractTests
{
    [Fact]
    public void Link_commands_guard_tenant_campus_dates_and_riders()
    {
        var create = Read("EduOS.Service/Services/Transport/TransportService.RouteVehicleCreate.cs");
        var close = Read("EduOS.Service/Services/Transport/TransportService.RouteVehicleClose.cs");
        Assert.Contains("IsolationLevel.Serializable", create);
        Assert.Contains("CanManageCampusAsync", create);
        Assert.Contains("overlapping", create);
        Assert.Contains("vehicle.Capacity", create);
        Assert.Contains("SequenceEqual(routeVersion)", create);
        Assert.Contains("IsolationLevel.Serializable", close);
        Assert.Contains("RouteVehicleAssignmentId == link.Id", close);
        Assert.Contains("SequenceEqual(version)", close);
    }
    [Fact]
    public void Authorized_api_and_ui_are_connected()
    {
        var api = Read("EduOS.App/Controllers/Api/TransportController.cs");
        var view = Read("EduOS.App/Views/TransportOperations/Index.cshtml");
        Assert.Contains("HttpPost(\"route-vehicle-links\")", api);
        Assert.Contains("HttpPost(\"route-vehicle-links/{id:long}/close\")", api);
        Assert.Contains("TenantAdmin,Principal,TransportManager", api);
        Assert.Contains("_RouteVehicleLinks", view);
        Assert.Contains("route-vehicle-create.js", view);
    }
    private static string Read(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while(dir != null)
        {
            var file = Path.Combine(dir.FullName,path.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(file))return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
