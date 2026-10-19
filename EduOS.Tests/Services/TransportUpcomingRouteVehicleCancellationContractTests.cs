using Xunit;
namespace EduOS.Tests.Services;
public class TransportUpcomingRouteVehicleCancellationContractTests
{
    [Fact]
    public void Cancel_future_link_requires_authorization_concurrency_and_no_riders()
    {
        var s=Read("EduOS.Service/Services/Transport/TransportService.RouteVehicleUpcomingCancellation.cs");
        Assert.Contains("IsolationLevel.Serializable",s);
        Assert.Contains("CanManageCampusAsync",s);
        Assert.Contains("SequenceEqual(version)",s);
        Assert.Contains("RouteVehicleAssignmentId == link.Id",s);
        Assert.Contains("TransportAssignmentState.Active",s);
        Assert.Contains("link.IsCurrent = false",s);
        Assert.Contains("UpdatedBy = _currentUser.UserId",s);
        Assert.Contains("if (!link.IsCurrent)",s);
    }
    [Fact]
    public void Cancellation_api_and_existing_operations_ui_are_wired()
    {
        Assert.Contains("route-vehicle-links/{id:long}/cancel-upcoming",Read("EduOS.App/Controllers/Api/TransportController.cs"));
        Assert.Contains("route-vehicle-cancel.js",Read("EduOS.App/Views/TransportOperations/Index.cshtml"));
        Assert.Contains("cancelUpcomingLink",Read("EduOS.App/wwwroot/js/transport-operations/route-vehicle-render.js"));
        Assert.Contains("x.IsCurrent && x.VehicleId",Read("EduOS.Service/Services/Transport/TransportService.RouteVehicleCreate.cs"));
        Assert.Contains("IsCancelled = !x.IsCurrent",Read("EduOS.Service/Services/Transport/TransportService.RouteVehicleQueries.cs"));
    }
    [Fact]
    public void Cancellation_handles_database_concurrency_and_transaction_conflicts()
    {
        var source = Read("EduOS.Service/Services/Transport/TransportService.RouteVehicleUpcomingCancellation.cs");
        Assert.Contains("catch (DbUpdateConcurrencyException)", source);
        Assert.Contains("catch (DbUpdateException ex)", source);
        Assert.Contains("catch (TransactionAbortedException ex)", source);
        Assert.Contains("ErrorResponse(\"Assignment changed. Reload and retry.\", 409)", source);
        Assert.Contains("IsolationLevel.Serializable", source);
    }
    private static string Read(string path)
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null)
        {
            var file=Path.Combine(dir.FullName,path.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(file))return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
