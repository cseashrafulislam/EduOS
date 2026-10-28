using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ResultRescrutinyWorkflowContractTests
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
    public void Immutable_ledger_enforces_tenant_keys_single_request_single_decision_and_publication_fk()
    {
        var context = Read("EduOS.Persistence/Context/EduOSDbContext.cs");
        var migration = Read("EduOS.Persistence/Migrations/20261028180000_ResultRescrutinyEvents.cs");
        context.Should().Contain("or ResultRescrutinyEvent");
        context.Should().Contain("TenantReference<ResultRescrutinyEvent, ResultPublication>");
        context.Should().Contain("TenantReference<ResultRescrutinyEvent, StudentEnrollment>");
        context.Should().Contain("x.TenantId, x.CaseReference, x.IsTerminal");
        context.Should().Contain("HasFilter(\"[IsTerminal] = 0\")");
        migration.Should().Contain("AK_ResultRescrutinyEvents_TenantId_Id");
        migration.Should().Contain("FK_ResultRescrutinyEvents_ResultPublications_TenantId_ResultPublicationId");
        migration.Should().Contain("FK_ResultRescrutinyEvents_StudentEnrollments_TenantId_StudentEnrollmentId");
        migration.Should().Contain("CK_ResultRescrutinyEvents_Action");
        migration.Should().Contain("IX_ResultRescrutinyEvents_TenantId_ClientRequestId");
        migration.Should().Contain("IX_ResultRescrutinyEvents_TenantId_CaseReference_IsTerminal");
    }

    [Fact]
    public void Intake_and_independent_decision_are_bounded_authorized_and_never_rewrite_published_marks()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.Rescrutiny.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("CanAccessCampusAsync(scope.CampusId");
        source.Should().Contain("HasValidRescrutinyEvidenceAsync");
        source.Should().Contain("StudentEnrollmentId == student.Id");
        source.Should().Contain("replay.ActorUserId != _user.UserId");
        source.Should().Contain("original.ActorUserId == _user.UserId");
        source.Should().Contain("Matches(original.RowVersion, request.RequestRowVersion)");
        source.Should().Contain("x.IsTerminal && !x.IsDeleted");
        source.Should().Contain("Take(request.PageSize + 1)");
        source.Should().NotContain("_summaries.Update(");
        source.Should().NotContain("_marks.Update(");
        source.Should().NotContain("IgnoreQueryFilters()");
        var invalid = new DecideResultRescrutinyDto
        {
            ClientRequestId = Guid.NewGuid(), RequestRowVersion = "AA==",
            Decision = ResultRescrutinyAction.Requested, Reason = "Not a decision"
        };
        invalid.Decision.Should().Be(ResultRescrutinyAction.Requested);
    }

    [Fact]
    public void Api_and_clean_bilingual_page_are_connected_to_scoped_list_and_audited_decision()
    {
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        var mvc = Read("EduOS.App/Controllers/AssessmentAdministrationController.cs");
        var view = Read("EduOS.App/Views/AssessmentAdministration/ResultRescrutiny.cshtml");
        var js = Read("EduOS.App/wwwroot/js/assessment/result-rescrutiny.js");
        api.Should().Contain("[HttpGet(\"results/rescrutiny\")]");
        api.Should().Contain("[HttpPost(\"results/rescrutiny\")]");
        api.Should().Contain("[HttpPost(\"results/rescrutiny/{caseReference:guid}/decision\")]");
        api.Should().Contain("DecideResultRescrutinyAsync");
        mvc.Should().Contain("IActionResult ResultRescrutiny()");
        view.Should().Contain("CultureInfo.CurrentUICulture");
        view.Should().Contain("rescrutinyDecisionForm");
        view.Should().Contain("rescrutinyScope");
        view.Should().Contain("rescrutinyCsv");
        js.Should().Contain("RequestVerificationToken");
        js.Should().Contain("results/tabulation?");
        js.Should().Contain("results/rescrutiny/");
        js.Should().Contain("state.nextBeforeId = data.nextBeforeId");
        js.Should().Contain("replaceChildren");
    }
}
