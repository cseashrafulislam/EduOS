using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportLegacyCampusRepairContractTests
{
    [Fact]
    public void Admin_review_and_apply_are_integrated_across_api_service_and_view()
    {
        var controller = typeof(TransportController);
        foreach (var action in new[] { nameof(TransportController.LegacyCampusReview),
            nameof(TransportController.ApplyLegacyCampusRepair) })
        {
            var method = controller.GetMethod(action)!;
            Assert.Contains(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        }
        var api = Read("EduOS.App", "Controllers", "Api", "TransportController.cs");
        var contract = Read("EduOS.Core", "Interfaces", "IServices", "ITransportService.cs");
        var service = Read("EduOS.Service", "Services", "Transport", "TransportService.LegacyCampusRepair.cs");
        var page = Read("EduOS.App", "Views", "TransportOperations", "Index.cshtml");
        var script = Read("EduOS.App", "wwwroot", "js", "transport-operations", "legacy-campus-review.js");
        Assert.Contains("HttpPost(\"legacy-campus-review/{kind}/{reference:guid}/apply\")", api);
        Assert.Contains("GetLegacyCampusRepairReviewAsync", contract);
        Assert.Contains("ApplyLegacyCampusRepairAsync", contract);
        Assert.Contains("_currentUser.IsTenantAdmin", service);
        Assert.Contains("request.ConfirmHistoricalCampusReview", service);
        Assert.Contains("IsolationLevel.Serializable", service);
        Assert.Contains("route.RowVersion.AsSpan().SequenceEqual(version)", service);
        Assert.Contains("vehicle.RowVersion.AsSpan().SequenceEqual(version)", service);
        Assert.Contains("_assignments.GetQueryable().IgnoreQueryFilters().AnyAsync", service);
        Assert.Contains("_vehicleRoutes.GetQueryable().IgnoreQueryFilters().AnyAsync", service);
        Assert.Contains("_driverLinks.GetQueryable().IgnoreQueryFilters().AnyAsync", service);
        Assert.Contains("_unitOfWork.SaveChangesAsync(ct)", service);
        Assert.Contains("<partial name=\"_LegacyCampusReview\" />", page);
        Assert.Contains("legacy-campus-review.js", page);
        Assert.Contains("RequestVerificationToken", script);
        Assert.Contains("confirmHistoricalCampusReview: true", script);
        Assert.Contains("textContent", script);
    }

    private static string Read(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
