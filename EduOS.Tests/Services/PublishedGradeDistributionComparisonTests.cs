using EduOS.Core.DTOs.Assessment;
using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PublishedGradeDistributionComparisonTests
{
    [Fact]
    public void Comparison_preserves_disappeared_new_and_withheld_grade_categories()
    {
        var baseline = new PublishedGradeDistributionDto
        {
            PublicationId = 10, VersionNo = 1, AssessmentName = "Exam",
            AcademicBatchName = "Cohort", StudentCount = 3, PassedCount = 2,
            FailedCount = 1, Grades =
            [
                new() { GradeLetter = "A", IsPassed = true, StudentCount = 2, AverageGpa = 4m },
                new() { GradeLetter = "F", StudentCount = 1, AverageGpa = 0m }
            ]
        };
        var target = new PublishedGradeDistributionDto
        {
            PublicationId = 11, VersionNo = 2, AssessmentName = "Exam",
            AcademicBatchName = "Cohort", StudentCount = 3, PassedCount = 2,
            WithheldCount = 1, Grades =
            [
                new() { GradeLetter = "A", IsPassed = true, StudentCount = 2, AverageGpa = 4.5m },
                new() { GradeLetter = "W", IsWithheld = true, StudentCount = 1 }
            ]
        };
        var result = PublishedGradeDistributionComparisonCalculator.Compare(baseline, target);
        result.StudentCountChange.Should().Be(0);
        result.BaselineFailedCount.Should().Be(1);
        result.TargetWithheldCount.Should().Be(1);
        result.Grades.Should().HaveCount(3);
        result.Grades.Single(x => x.GradeLetter == "F").CountChange.Should().Be(-1);
        result.Grades.Single(x => x.GradeLetter == "W").CountChange.Should().Be(1);
        result.Grades.Single(x => x.GradeLetter == "A").TargetAverageGpa.Should().Be(4.5m);
    }

    [Fact]
    public void Comparison_keeps_pass_and_fail_categories_separate_for_same_letter()
    {
        var baseline = new PublishedGradeDistributionDto
        {
            Grades = [new() { GradeLetter = "P", IsPassed = true, StudentCount = 4 }]
        };
        var target = new PublishedGradeDistributionDto
        {
            Grades = [new() { GradeLetter = "P", IsPassed = false, StudentCount = 4 }]
        };
        var result = PublishedGradeDistributionComparisonCalculator.Compare(baseline, target);
        result.Grades.Should().HaveCount(2);
        result.Grades.Single(x => x.IsPassed).CountChange.Should().Be(-4);
        result.Grades.Single(x => !x.IsPassed).CountChange.Should().Be(4);
    }

    [Fact]
    public void Comparison_api_and_report_are_guarded_and_version_pinned()
    {
        static string Read(string path)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) return File.ReadAllText(file);
            }
            throw new FileNotFoundException(path);
        }
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.GradeComparison.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("x.TenantId == tenant && !x.IsDeleted");
        source.Should().Contain("baseline.AcademicBatchId != target.AcademicBatchId");
        source.Should().Contain("baseline.VersionNo >= target.VersionNo");
        source.Should().Contain("GetPublishedGradeDistributionAsync");
        source.Should().NotContain("IgnoreQueryFilters()");
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        api.Should().Contain("[HttpGet(\"results/grade-distribution/compare\")]");
        var view = Read("EduOS.App/Views/AssessmentAdministration/GradeDistribution.cshtml");
        view.Should().Contain("gradeBaseline");
        view.Should().Contain("gradeCompareRows");
    }
}
