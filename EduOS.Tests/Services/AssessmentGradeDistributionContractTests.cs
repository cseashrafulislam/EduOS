using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentGradeDistributionContractTests
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
    public void Grade_report_rejects_missing_publication_identifier()
    {
        var invalid = new PublishedGradeDistributionQueryDto { PublicationId = 0 };
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(invalid, new ValidationContext(invalid), errors, true)
            .Should().BeFalse();
        var valid = new PublishedGradeDistributionQueryDto { PublicationId = 1 };
        Validator.TryValidateObject(valid, new ValidationContext(valid),
            new List<ValidationResult>(), true).Should().BeTrue();
    }

    [Fact]
    public void Grade_report_is_tenant_campus_scoped_snapshot_only_and_sql_aggregated()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.GradeDistribution.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("x.TenantId == tenant");
        source.Should().Contain("CanAccessCampusAsync(scope.CampusId, ct)");
        source.Should().Contain("GetPublicationEvidenceAsync([publication.Id]");
        source.Should().Contain("!proof.IsReconciled");
        source.Should().Contain("x.PublicationVersionNo == publication.VersionNo");
        source.Should().Contain(".GroupBy(x => new { x.GradeLetter, x.IsPassed, x.IsWithheld })");
        source.Should().Contain(".Take(101).ToListAsync(ct)");
        source.Should().Contain("grouped.Sum(x => x.Count) != proof.ActiveCount");
        source.Should().NotContain("IgnoreQueryFilters()");
        source.Should().NotContain("ToListAsync(ct);\n        var grouped");
    }

    [Fact]
    public void Grade_report_has_an_authorized_api_and_clean_page()
    {
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        var controller = Read("EduOS.App/Controllers/AssessmentAdministrationController.cs");
        var view = Read("EduOS.App/Views/AssessmentAdministration/GradeDistribution.cshtml");
        api.Should().Contain("[HttpGet(\"results/grade-distribution\")]");
        api.Should().Contain("GetPublishedGradeDistributionAsync");
        controller.Should().Contain("IActionResult GradeDistribution()");
        view.Should().Contain("gradeReportTitle");
        view.Should().Contain("gradeScope");
        view.Should().Contain("gradeVersion");
        view.Should().Contain("Grade distribution help");
    }
}
