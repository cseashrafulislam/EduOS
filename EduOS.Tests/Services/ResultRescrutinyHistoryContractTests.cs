using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ResultRescrutinyHistoryContractTests
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
    public void History_is_tenant_and_campus_scoped_bounded_and_reconciled()
    {
        var service = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.RescrutinyHistory.cs");
        service.Should().Contain("if (!CanPublish())");
        service.Should().Contain("x.TenantId == tenant && x.CaseReference == caseReference");
        service.Should().Contain("CanAccessCampusAsync(scope.CampusId");
        service.Should().Contain("HasValidRescrutinyEvidenceAsync(scope");
        service.Should().Contain("enrollment.AcademicBatchId == scope.Publication.AcademicBatchId");
        service.Should().Contain("Take(3)");
        service.Should().Contain("events.Count is < 1 or > 2");
        service.Should().Contain("events[1].ActorUserId == original.ActorUserId");
        service.Should().NotContain("IgnoreQueryFilters()");
        service.Should().NotContain("_summaries.Update(");
    }

    [Fact]
    public void Status_filter_and_history_route_reuse_existing_authorized_workflow()
    {
        var service = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Rescrutiny.cs");
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        var contract = Read("EduOS.Core/Interfaces/IServices/IAssessmentAdministrationService.cs");
        service.Should().Contain("decisionsForStatus.Any(");
        service.Should().Contain("Take(request.PageSize + 1)");
        api.Should().Contain("results/rescrutiny/{caseReference:guid}/history");
        contract.Should().Contain("GetResultRescrutinyHistoryAsync");
    }
}
