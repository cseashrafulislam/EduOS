using EduOS.Core.DTOs.Assessment;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ResultFollowUpContractTests
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

    [Theory]
    [InlineData(ResultFollowUpStatus.Failed)]
    [InlineData(ResultFollowUpStatus.Withheld)]
    [InlineData(ResultFollowUpStatus.Both)]
    public void Outcome_register_uses_valid_explicit_categories(ResultFollowUpStatus status)
    {
        var query = new ResultFollowUpQueryDto { PublicationId = 1, Status = status };
        query.PageSize.Should().BeLessThanOrEqualTo(50);
        query.Status.Should().BeOneOf(ResultFollowUpStatus.Failed,
            ResultFollowUpStatus.Withheld, ResultFollowUpStatus.Both);
    }

    [Fact]
    public void Sql_report_is_version_pinned_tenant_scoped_bounded_and_fail_closed()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.ResultFollowUp.cs");
        source.Should().Contain("CanPublish()");
        source.Should().Contain("CanAccessCampusAsync(scope.CampusId");
        source.Should().Contain("GetPublicationEvidenceAsync");
        source.Should().Contain("evidence.IsReconciled");
        source.Should().Contain("summary.TenantId == tenant");
        source.Should().Contain("summary.PublicationVersionNo == publication.VersionNo");
        source.Should().Contain("!x.summary.IsPassed && !x.summary.IsWithheld");
        source.Should().Contain("query.CountAsync(ct)");
        source.Should().Contain("Take(request.PageSize)");
        source.Should().NotContain("IgnoreQueryFilters()");
        source.Should().NotContain("_summaries.Update(");
    }

    [Fact]
    public void Scoped_api_and_bilingual_page_are_connected()
    {
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        var mvc = Read("EduOS.App/Controllers/AssessmentAdministrationController.cs");
        var view = Read("EduOS.App/Views/AssessmentAdministration/ResultFollowUp.cshtml");
        api.Should().Contain("results/follow-up");
        api.Should().Contain("SearchResultFollowUpAsync");
        mvc.Should().Contain("IActionResult ResultFollowUp()");
        view.Should().Contain("CultureInfo.CurrentUICulture");
        view.Should().Contain("followUpCsv");
        view.Should().Contain("followUpPrint");
    }
}
