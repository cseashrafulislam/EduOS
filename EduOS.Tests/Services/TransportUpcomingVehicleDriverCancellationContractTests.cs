using Xunit;
namespace EduOS.Tests.Services;
public sealed class TransportUpcomingVehicleDriverCancellationContractTests
{
    [Fact]
    public void Cancellation_uses_canonical_aggregate_and_guards()
    {
        var source = Read("EduOS.Service","Services","Transport","TransportService.VehicleDriverUpcomingCancellation.cs");
        Assert.Contains("_driverLinks.GetQueryable()",source);
        Assert.Contains("CanManageDriverRegistry()",source);
        Assert.Contains("CanManageCampusAsync",source);
        Assert.Contains("IsolationLevel.Serializable",source);
        Assert.Contains("SequenceEqual(version)",source);
        Assert.Contains("TransportAssignmentState.Active",source);
        Assert.Contains("link.IsCurrent = false",source);
        Assert.Contains("link.UpdatedBy = _currentUser.UserId",source);
        Assert.Contains("DbUpdateConcurrencyException",source);
        Assert.DoesNotContain("MonthlyFare =",source);
    }
    [Fact]
    public void Authorized_api_and_ui_are_wired_to_existing_workspace()
    {
        var api=Read("EduOS.App","Controllers","Api","TransportController.cs");
        var ui=Read("EduOS.App","wwwroot","js","transport-operations","vehicle-driver-list.js");
        var view=Read("EduOS.App","Views","TransportOperations","_VehicleDriverLinks.cshtml");
        Assert.Contains("HttpPost(\"vehicle-driver-links/{id:long}/cancel-upcoming\")",api);
        Assert.Contains("Authorize(Roles = \"TenantAdmin\")",api);
        Assert.Contains("scheduledOnly",api);
        Assert.Contains("cancel-upcoming",ui);
        Assert.Contains("rowVersion:item.rowVersion",ui);
        Assert.Contains("transportVehicleDriverScheduledOnly",view);
    }
    private static string Read(params string[] segments)
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null)
        {
            var file=Path.Combine(new[]{dir.FullName}.Concat(segments).ToArray());
            if(File.Exists(file))return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException("Missing repository source: "+Path.Combine(segments));
    }
}
