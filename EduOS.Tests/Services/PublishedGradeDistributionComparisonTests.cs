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
        result.Grades.Single(x => x.GradeLetter == "F").ObtainedMarksChange.Should().Be(0m);
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
    public void Comparison_reports_exact_historical_marks_and_percentage_deltas_without_inventing_missing_averages()
    {
        var baseline = new PublishedGradeDistributionDto
        {
            Grades = [
                new() { GradeLetter = "A", IsPassed = true, StudentCount = 2,
                    TotalObtainedMarks = 170m, AveragePercentage = 85.1250m },
                new() { GradeLetter = "F", StudentCount = 1, TotalObtainedMarks = 20m }
            ]
        };
        var target = new PublishedGradeDistributionDto
        {
            Grades = [
                new() { GradeLetter = "A", IsPassed = true, StudentCount = 2,
                    TotalObtainedMarks = 180m, AveragePercentage = 90.3333m },
                new() { GradeLetter = "W", IsWithheld = true, StudentCount = 1,
                    TotalObtainedMarks = 0m }
            ]
        };
        var rows = PublishedGradeDistributionComparisonCalculator.Compare(baseline, target).Grades;
        var a = rows.Single(x => x.GradeLetter == "A");
        a.BaselineTotalObtainedMarks.Should().Be(170m);
        a.TargetTotalObtainedMarks.Should().Be(180m);
        a.ObtainedMarksChange.Should().Be(10m);
        a.AveragePercentageChange.Should().Be(5.2083m);
        var removed = rows.Single(x => x.GradeLetter == "F");
        removed.ObtainedMarksChange.Should().Be(-20m);
        removed.AveragePercentageChange.Should().BeNull();
        var added = rows.Single(x => x.GradeLetter == "W");
        added.ObtainedMarksChange.Should().Be(0m);
        added.AveragePercentageChange.Should().BeNull();
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
