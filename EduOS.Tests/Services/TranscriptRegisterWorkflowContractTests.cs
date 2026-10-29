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
        var service = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.TranscriptRegister.cs");
        var issue = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.Documents.cs");
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
        var source = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.TranscriptRegister.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("issue.TenantId == tenant && !issue.IsDeleted");
        source.Should().Contain("student.TenantId");
        source.Should().Contain("Take(request.PageSize)");
        source.Should().Contain("RowVersion = Version(x.issue.RowVersion)");
        source.Should().Contain("eligible.Contains(x.Id)");
        source.Should().Contain("SearchTranscriptCandidatesAsync(search, 1, 30, ct)");
        source.Should().Contain(".Skip((page - 1) * pageSize).Take(pageSize)");
    }

    [Fact]
    public void Controller_and_navigation_restrict_transcript_management_to_authorized_roles()
    {
        var controller = Read("EduOS.App", "Controllers", "Api", "AssessmentAdministrationApiController.cs");
        var mvc = Read("EduOS.App", "Controllers", "AssessmentAdministrationController.cs");
        var nav = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        controller.Should().Contain("[HttpGet(\"transcripts/candidates\")]");
        controller.Should().Contain("[HttpGet(\"transcripts\")]");
        controller.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("public IActionResult Transcripts() => View()");
        nav.Should().Contain("href=\"/AssessmentAdministration/Transcripts\"");
    }

    [Fact]
    public void Browser_workflow_supports_csrf_idempotency_revoke_and_safe_csv()
    {
        var script = Read("EduOS.App", "wwwroot", "js", "assessment", "transcripts.js");
        var view = Read("EduOS.App", "Views", "AssessmentAdministration", "Transcripts.cshtml");
        script.Should().Contain("RequestVerificationToken: token");
        script.Should().Contain("state.requestId ||= crypto.randomUUID()");
        script.Should().Contain("rowVersion: row.rowVersion");
        script.Should().Contain("URL.createObjectURL");
        script.Should().Contain("replace(/");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("id=\"transcriptRows\"");
        view.Should().Contain("authenticated, immutable academic documents");
    }

    [Fact]
    public void Formal_transcript_print_uses_frozen_protected_evidence_and_revalidates_publication_state()
    {
        var issuer = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.Documents.cs");
        var document = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.TranscriptDocument.cs");
        var controller = Read("EduOS.App", "Controllers", "AssessmentAdministrationController.cs");
        var view = Read("EduOS.App", "Views", "AssessmentAdministration", "TranscriptDocument.cshtml");
        var migration = Read("EduOS.Persistence", "Migrations", "20261029170000_ProtectedTranscriptEvidence.cs");
        issuer.Should().Contain("ProtectedAcademicRecord = _transcriptProtector.Protect(serialized)");
        issuer.Should().Contain("TranscriptPublicationsStillValidAsync(document, token)");
        document.Should().Contain("CanAccessStudentHistoryAsync(issue.StudentId, ct)");
        document.Should().Contain("TranscriptSnapshotIntegrity.TryRead(_transcriptProtector, issue");
        document.Should().Contain("TranscriptPublicationsStillValidAsync(snapshot!, ct)");
        controller.Should().Contain("public async Task<IActionResult> TranscriptDocument(Guid reference");
        controller.Should().Contain("[Authorize(Roles = \"TenantAdmin,Principal,VicePrincipal,ExamController\")]");
        view.Should().Contain("@model EduOS.Core.DTOs.Assessment.TranscriptSnapshotDocumentDto");
        view.Should().Contain("window.print()");
        migration.Should().Contain("nullable: true");
    }
}
