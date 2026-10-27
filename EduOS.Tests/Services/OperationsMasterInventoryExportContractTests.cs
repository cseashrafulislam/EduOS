using System;
using System.IO;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class OperationsMasterInventoryExportContractTests
{
    private static string Read(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }

    [Theory]
    [InlineData("Hostel", "HostelService.FacilitiesExport.cs", "GetFacilitiesAsync", "HostelController.cs", "facilities/export", "HostelOperations/Index.cshtml", "hostelFacilityCsv", "hostel-operations/facilities.js")]
    [InlineData("Transport", "TransportService.RouteSetupExport.cs", "GetRouteSetupPageAsync", "TransportController.cs", "route-setup/export", "TransportOperations/Index.cshtml", "transportRouteSetupCsv", "transport-operations/route-setup.js")]
    public void Export_IsCampusAuthorizedBoundedAndWiredThroughUi(string domain, string serviceFile,
        string query, string apiFile, string endpoint, string viewFile, string button, string scriptFile)
    {
        var service = Read($"EduOS.Service/Services/{domain}/{serviceFile}");
        var api = Read($"EduOS.App/Controllers/Api/{apiFile}");
        var view = Read($"EduOS.App/Views/{viewFile}");
        var script = Read($"EduOS.App/wwwroot/js/{scriptFile}");
        Assert.Contains(query + "(campusId, 1, 100, search, ct)", service);
        Assert.Contains("count > 1000", service);
        Assert.Contains("next.Data.TotalCount != count", service);
        Assert.Contains("rows.Count != count", service);
        Assert.Contains("CsvCell(x.Name)", service);
        Assert.DoesNotContain("StudentId", service);
        Assert.Contains($"[HttpGet(\"{endpoint}\")]", api);
        Assert.Contains("Authorize(Roles", api);
        Assert.Contains(button, view);
        Assert.Contains("loadedFilter", script);
        Assert.Contains(endpoint + "?", script);
    }
}
