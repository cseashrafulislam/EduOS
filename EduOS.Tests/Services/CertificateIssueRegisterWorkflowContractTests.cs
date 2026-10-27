using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CertificateIssueRegisterWorkflowContractTests
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
    public void Certificate_candidate_and_issue_share_passed_published_result_eligibility()
    {
        var register = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.CertificateRegister.cs");
        var write = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.Documents.cs");
        register.Should().Contain("private IQueryable<long> EligibleCertificateEnrollmentIds");
        register.Should().Contain("summary.IsPassed");
        register.Should().Contain("publication.State == ResultPublicationState.Published");
        register.Should().Contain("summary.PublicationVersionNo == publication.VersionNo");
        register.Should().Contain("enrollment.CampusId == batch.CampusId");
        register.Should().Contain("assessment.AcademicYearId == enrollment.AcademicYearId");
        write.Should().Contain("EligibleCertificateEnrollmentIds(tenant, request.IssueDate)");
        write.Should().Contain("replay.StudentEnrollmentId");
    }

    [Fact]
    public void Register_is_tenant_scoped_bounded_and_returns_revoke_version()
    {
        var register = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.CertificateRegister.cs");
        register.Should().Contain("if (!CanPublish())");
        register.Should().Contain("issue.TenantId == tenant && !issue.IsDeleted");
        register.Should().Contain("Take(request.PageSize)");
        register.Should().Contain("RowVersion = Version(x.issue.RowVersion)");
        register.Should().Contain("SearchCertificateCandidatesAsync(search, 1, 30, ct)");
        register.Should().Contain(".Skip((page - 1) * pageSize).Take(pageSize)");
    }

    [Fact]
    public void Api_mvc_and_menu_restrict_certificate_administration()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "AssessmentAdministrationApiController.cs");
        var mvc = Read("EduOS.App", "Controllers", "AssessmentAdministrationController.cs");
        var menu = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        api.Should().Contain("[HttpGet(\"certificates/candidates\")]");
        api.Should().Contain("[HttpGet(\"certificates\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("public IActionResult Certificates() => View()");
        menu.Should().Contain("href=\"/AssessmentAdministration/Certificates\"");
    }

    [Fact]
    public void Browser_has_csrf_idempotency_concurrency_and_safe_csv()
    {
        var js = Read("EduOS.App", "wwwroot", "js", "assessment", "certificates.js");
        var view = Read("EduOS.App", "Views", "AssessmentAdministration", "Certificates.cshtml");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("state.requestId ||= crypto.randomUUID()");
        js.Should().Contain("rowVersion: row.rowVersion");
        js.Should().Contain("td.textContent");
        js.Should().Contain("URL.createObjectURL");
        js.Should().Contain("replace(/");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("id=\"certificateRegisterRows\"");
        view.Should().Contain("does not create a signed PDF");
    }
}
