using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentComponentRegisterWorkflowContractTests
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

    [Theory]
    [InlineData(0, 20, false)]
    [InlineData(1, 101, false)]
    [InlineData(1, 20, true)]
    public void Query_has_bounded_pagination(int page, int pageSize, bool expected)
    {
        var query = new AssessmentComponentRegisterQueryDto { Page = page, PageSize = pageSize };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(query, new ValidationContext(query), results, true).Should().Be(expected);
    }

    [Fact]
    public void Register_joins_canonical_scopes_and_enforces_tenant_campus_year()
    {
        var source = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.ComponentRegister.cs");
        source.Should().Contain("component.TenantId == tenant && subject.TenantId == tenant");
        source.Should().Contain("exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId");
        source.Should().Contain("Take(request.PageSize)");
        source.Should().Contain("Version(x.RowVersion)");
    }

    [Fact]
    public void Existing_write_enforces_draft_marks_weight_concurrency_and_scope()
    {
        var source = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.Documents.cs");
        source.Should().Contain("exam.State != AssessmentState.Draft");
        source.Should().Contain("batch.CampusId != exam.CampusId");
        source.Should().Contain("Matches(old.RowVersion, request.RowVersion)");
        source.Should().Contain("siblings.Sum(x => x.Weightage) + request.Weightage > 100m");
        source.Should().Contain("Component with existing marks requires an audited amendment.");
    }

    [Fact]
    public void Api_mvc_navigation_and_browser_are_connected_and_safe()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "AssessmentAdministrationApiController.cs");
        var mvc = Read("EduOS.App", "Controllers", "AssessmentAdministrationController.cs");
        var menu = Read("EduOS.App", "Views", "Shared", "_Layout.cshtml");
        var view = Read("EduOS.App", "Views", "AssessmentAdministration", "AssessmentComponents.cshtml");
        var js = Read("EduOS.App", "wwwroot", "js", "assessment", "assessment-components.js");
        api.Should().Contain("[HttpGet(\"components\")]");
        mvc.Should().Contain("public IActionResult AssessmentComponents() => View()");
        menu.Should().Contain("href=\"/AssessmentAdministration/AssessmentComponents\"");
        view.Should().Contain("@Html.AntiForgeryToken()");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("rowVersion: state.rowVersion");
        js.Should().Contain("td.textContent");
        js.Should().Contain("URL.createObjectURL");
        js.Should().Contain("Check the register before retrying");
    }
}
