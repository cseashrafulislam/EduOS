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
        api.Should().Contain("results/follow-up/summary");
        api.Should().Contain("results/follow-up/export");
        api.Should().Contain("GetResultFollowUpSummaryAsync");
        api.Should().Contain("ExportResultFollowUpAsync");
        api.Should().Contain("SearchResultFollowUpAsync");
        mvc.Should().Contain("IActionResult ResultFollowUp()");
        view.Should().Contain("CultureInfo.CurrentUICulture");
        view.Should().Contain("followUpCsv");
        view.Should().Contain("followUpSummary");
        view.Should().Contain("followUpPrint");
    }
    [Fact]
    public void Full_export_and_summary_reuse_authorized_snapshot_checks_and_are_bounded()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.ResultFollowUp.cs");
        source.Should().Contain("SearchResultFollowUpAsync(new()");
        source.Should().Contain("ResultFollowUpStatus.Failed");
        source.Should().Contain("ResultFollowUpStatus.Withheld");
        source.Should().Contain("for (var page = 1; page <= 10; page++)");
        source.Should().Contain("PageSize = 50");
        source.Should().Contain("data.TotalCount != expectedTotal");
        source.Should().Contain("Duplicate follow-up evidence requires reconciliation.");
        source.Should().Contain("Truncated = items.Count < expectedTotal.Value");
        source.Should().NotContain("IgnoreQueryFilters()");
    }

    [Fact]
    public void Export_contract_caps_rows_and_reports_truncation()
    {
        var dto = new ResultFollowUpExportDto { PublicationId = 42, TotalCount = 501,
            Truncated = true };
        dto.Limit.Should().Be(500);
        dto.Truncated.Should().BeTrue();
        new ResultFollowUpSummaryDto { Failed = 3, Withheld = 2 }.Total.Should().Be(5);
    }
}
