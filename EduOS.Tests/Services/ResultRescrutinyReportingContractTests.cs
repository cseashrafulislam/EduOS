using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ResultRescrutinyReportingContractTests
{
    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }

    [Fact]
    public void Reporting_reuses_tenant_campus_snapshot_and_case_integrity_gates()
    {
        var service = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.RescrutinyReporting.cs");
        service.Should().Contain("CanPublish()");
        service.Should().Contain("FindRescrutinyScopeAsync");
        service.Should().Contain("CanAccessCampusAsync(scope.CampusId");
        service.Should().Contain("HasValidRescrutinyEvidenceAsync");
        service.Should().Contain("x.TenantId == tenant");
        service.Should().Contain("r.StudentEnrollmentId == d.StudentEnrollmentId");
        service.Should().Contain("LongCountAsync");
        service.Should().Contain("pageNo < 10");
        service.Should().Contain("SearchResultRescrutinyAsync");
        service.Should().NotContain("IgnoreQueryFilters()");
        service.Should().NotContain("_summaries.Update(");
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        api.Should().Contain("results/rescrutiny/summary");
        api.Should().Contain("results/rescrutiny/export");
        api.Should().Contain("GetResultRescrutinySummaryAsync");
        api.Should().Contain("ExportResultRescrutinyAsync");
    }
}
