using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentComponentMarksWorkspaceContractTests
{
    private static string Read(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var file = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    [Fact]
    public void Roster_query_requires_scope()
    {
        var query = new AssessmentComponentMarkRosterQueryDto { AssessmentId = 1, AcademicBatchId = 1, AssessmentSubjectId = 0 };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(query, new ValidationContext(query), errors, true).Should().BeFalse();
        query.AssessmentSubjectId = 1;
        errors.Clear();
        Validator.TryValidateObject(query, new ValidationContext(query), errors, true).Should().BeTrue();
    }

    [Fact]
    public void Roster_reuses_canonical_eligibility_and_enforces_tenant()
    {
        var source = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.ComponentMarks.cs");
        source.Should().Contain("SearchMarkRosterAsync(new AssessmentMarkRosterPageQueryDto");
        source.Should().Contain("Page = request.Page, PageSize = request.PageSize, Search = request.Search");
        source.Should().Contain("TotalCount = roster.TotalCount");
        source.Should().Contain("x.TenantId == tenant");
        source.Should().Contain("roster.IsResultPublished");
        source.Should().Contain("AssessmentState.MarksEntry");
        source.Should().Contain("StudentAssessmentMarkId = master?.Id");
    }

    [Fact]
    public void Component_writes_block_published_and_ineligible_records()
    {
        var source = Read("EduOS.Service", "Services", "Assessment", "AssessmentAdministrationService.Documents.cs");
        source.Should().Contain("Published or withdrawn component marks require an approved, audited amendment.");
        source.Should().Contain("HasFinalizedPublicationAsync(exam.Id, batch.Id, token)");
        source.Should().Contain("EligibleAsync(offering.Id, batch.Id, token)");
        source.Should().Contain("batch.CampusId != exam.CampusId");
        source.Should().Contain("Component mark already recorded.");
        source.Should().Contain("Matches(row.RowVersion, request.RowVersion)");
    }

    [Fact]
    public void End_to_end_api_view_menu_and_safe_browser_are_connected()
    {
        Read("EduOS.App", "Controllers", "Api", "AssessmentAdministrationApiController.cs")
            .Should().Contain("[HttpGet(\"component-marks/roster\")]");
        Read("EduOS.App", "Controllers", "AssessmentAdministrationController.cs")
            .Should().Contain("public IActionResult ComponentMarks() => View()");
        Read("EduOS.App", "Views", "Shared", "_Layout.cshtml")
            .Should().Contain("href=\"/AssessmentAdministration/ComponentMarks\"");
        Read("EduOS.App", "Views", "AssessmentAdministration", "ComponentMarks.cshtml")
            .Should().Contain("@Html.AntiForgeryToken()");
        var js = Read("EduOS.App", "wwwroot", "js", "assessment", "component-marks.js");
        js.Should().Contain("RequestVerificationToken: token");
        js.Should().Contain("td.textContent");
        js.Should().Contain("rowVersion: tr.dataset.version");
        js.Should().Contain("Review refreshed records before retrying");
        js.Should().Contain("URL.createObjectURL");
    }
}
