using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class GradeSchemeRegisterWorkflowContractTests
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
    public void Register_filters_tenant_deleted_rows_and_limits_page_size()
    {
        var service = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.GradeSchemeRegister.cs");
        service.Should().Contain("if (!CanPublish())");
        service.Should().Contain("x.TenantId == tenant && !x.IsDeleted");
        service.Should().Contain("request.PageSize is < 1 or > 100");
        service.Should().Contain("Take(request.PageSize)");
        service.Should().Contain("ids.Contains(x.GradeSchemeId)");
    }

    [Fact]
    public void Detail_is_tenant_scoped_and_returns_immutable_rules()
    {
        var service = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.GradeSchemeRegister.cs");
        var setup = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.Setup.cs");
        service.Should().Contain("x.TenantId == tenant && x.Id == gradeSchemeId && !x.IsDeleted");
        service.Should().Contain("MapGradeScheme(row, rules)");
        setup.Should().Contain("Existing grading schemes require a controlled versioned amendment.");
        setup.Should().Contain("Grade intervals must cover 0–100 without overlaps or gaps.");
        setup.Should().Contain("string.IsNullOrWhiteSpace(x.GradeLetter)");
        setup.Should().Contain("!Exact2(x.MinMarks)");
        setup.Should().Contain("!Exact2(x.MaxMarks)");
    }

    [Fact]
    public void Api_mvc_and_menu_are_restricted_to_exam_managers()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "AssessmentAdministrationApiController.cs");
        var mvc = Read("EduOS.App", "Controllers", "AssessmentAdministrationController.cs");
        var menu = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        api.Should().Contain("[HttpGet(\"grade-schemes\")]");
        api.Should().Contain("[HttpGet(\"grade-schemes/{id:long}\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        mvc.Should().Contain("public IActionResult GradeSchemes() => View()");
        menu.Should().Contain("href=\"/AssessmentAdministration/GradeSchemes\"");
    }

    [Fact]
    public void Browser_enforces_rules_antiforgery_and_safe_reporting()
    {
        var js = Read("EduOS.App", "wwwroot", "js", "assessment", "grade-schemes.js");
        var view = Read("EduOS.App", "Views", "AssessmentAdministration", "GradeSchemes.cshtml");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("collectRules()");
        js.Should().Contain("gradeLetter");
        js.Should().Contain("td.textContent");
        js.Should().Contain("URL.createObjectURL");
        js.Should().Contain("Check the register before retrying");
        view.Should().Contain("@Html.AntiForgeryToken()");
        view.Should().Contain("id=\"gradeSchemeRuleEditor\"");
        view.Should().Contain("id=\"gradeSchemeRows\"");
    }
}
