using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentAdmitCardWorkflowContractTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var file = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Candidate_cards_require_privileged_campus_scoped_tenant_and_active_enrollment()
    {
        var source = Read("EduOS.Service", "Services", "Assessment",
            "AssessmentAdministrationService.AdmitCards.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("var scope = await ScopeAsync(request, ct)");
        source.Should().Contain("enrollment.TenantId == tenant && student.TenantId == tenant");
        source.Should().Contain("enrollment.CampusId == exam.CampusId");
        source.Should().Contain("enrollment.IsCurrent && enrollment.State == EnrollmentState.Active");
        source.Should().Contain("reg.TenantId == tenant && !reg.IsDeleted");
    }

    [Fact]
    public void Incomplete_schedules_duplicate_registrations_and_unbounded_reports_fail_closed()
    {
        var source = Read("EduOS.Service", "Services", "Assessment",
            "AssessmentAdministrationService.AdmitCards.cs");
        source.Should().Contain("request.PageSize is < 1 or > 50");
        source.Should().Contain("subjects.Count > 100");
        source.Should().Contain("Assessment subjects require reconciliation.");
        source.Should().Contain("Exam schedule is incomplete or inconsistent.");
        source.Should().Contain("Duplicate candidate registrations require reconciliation.");
        source.Should().Contain("Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)");
        source.Should().Contain("roomIds.Contains(x.Id)");
    }

    [Fact]
    public void Controller_and_browser_protect_card_access_and_export()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "AssessmentAdministrationApiController.cs");
        var mvc = Read("EduOS.App", "Controllers", "AssessmentAdministrationController.cs");
        var js = Read("EduOS.App", "wwwroot", "js", "assessment", "admit-cards.js");
        api.Should().Contain("[HttpGet(\"admit-cards\")]");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin,Principal,VicePrincipal,ExamController\")]");
        mvc.Should().Contain("public IActionResult AdmitCards() => View()");
        js.Should().Contain("credentials: 'same-origin'");
        js.Should().Contain("cache: 'no-store'");
        js.Should().Contain("node.textContent = value");
        js.Should().Contain("rows.map(row => row.map(csvValue)");
        js.Should().Contain("window.print()");
    }
}
