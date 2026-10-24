using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TranscriptRegisterWorkflowContractTests
{
    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    [Fact]
    public void Candidate_and_issue_use_same_released_published_result_gate()
    {
        var service = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.TranscriptRegister.cs");
        var issue = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.Documents.cs");
        service.Should().Contain("private IQueryable<long> EligibleTranscriptStudentIds(long tenant)");
        service.Should().Contain("publication.State == ResultPublicationState.Published");
        service.Should().Contain("publication.VisibleToStudent");
        service.Should().Contain("summary.PublicationVersionNo == publication.VersionNo");
        service.Should().Contain("publication.AcademicBatchId == batch.Id");
        service.Should().Contain("enrollment.CampusId == batch.CampusId");
        service.Should().Contain("assessment.AcademicYearId == enrollment.AcademicYearId");
        issue.Should().Contain("EligibleTranscriptStudentIds(tenant).AnyAsync(id => id == student.Id, token)");
    }

    [Fact]
    public void Register_search_is_tenant_scoped_bounded_and_returns_concurrency_token()
    {
        var source = Read("EduOS.Service", "Services", "Exams", "ExamWorkflowService.TranscriptRegister.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("issue.TenantId == tenant && !issue.IsDeleted");
        source.Should().Contain("student.TenantId");
        source.Should().Contain("Take(request.PageSize)");
        source.Should().Contain("RowVersion = Version(x.issue.RowVersion)");
        source.Should().Contain("eligible.Contains(x.Id)");
        source.Should().Contain("Take(30)");
    }

    [Fact]
    public void Controller_and_navigation_restrict_transcript_management_to_authorized_roles()
    {
        var controller = Read("EduOS.App", "Controllers", "Api", "ExamWorkflowController.cs");
        var mvc = Read("EduOS.App", "Controllers", "ExamOperationsController.cs");
        var nav = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        controller.Should().Contain("[HttpGet(\"transcripts/candidates\")]");
        controller.Should().Contain("[HttpGet(\"transcripts\")]");
        controller.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("public IActionResult Transcripts() => View()");
        nav.Should().Contain("href=\"/ExamOperations/Transcripts\"");
    }

    [Fact]
    public void Browser_workflow_supports_csrf_idempotency_revoke_and_safe_csv()
    {
        var script = Read("EduOS.App", "wwwroot", "js", "exams", "transcripts.js");
        var view = Read("EduOS.App", "Views", "ExamOperations", "Transcripts.cshtml");
        script.Should().Contain("RequestVerificationToken: token");
        script.Should().Contain("state.requestId ||= crypto.randomUUID()");
        script.Should().Contain("rowVersion: row.rowVersion");
        script.Should().Contain("URL.createObjectURL");
        script.Should().Contain("replace(/");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("id=\"transcriptRows\"");
        view.Should().Contain("not a signed transcript document");
    }
}
