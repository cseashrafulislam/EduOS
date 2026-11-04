using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ExamReportContractTests
{
    [Fact]
    public void All_nineteen_original_report_identifiers_are_stable()
    {
        var values = Enum.GetValues<ExamReportKind>().Select(x => (int)x).OrderBy(x => x).ToArray();
        Assert.Equal(Enumerable.Range(1, 19).ToArray(), values);
    }

    [Fact]
    public void Report_pages_and_subject_search_are_bounded()
    {
        Assert.False(Valid(new ExamReportQueryDto { PublicationId = 0, Kind = ExamReportKind.Tabulation }));
        Assert.False(Valid(new ExamReportQueryDto { PublicationId = 1, Kind = ExamReportKind.Tabulation, PageSize = 51 }));
        Assert.False(Valid(new ExamReportQueryDto { PublicationId = 1, Kind = ExamReportKind.Tabulation, Page = 0 }));
        Assert.False(Valid(new ExamReportQueryDto { PublicationId = 1, Kind = ExamReportKind.Tabulation, Search = new string('a', 101) }));
        Assert.True(Valid(new ExamReportQueryDto { PublicationId = 1, Kind = ExamReportKind.SubjectWiseFail, AssessmentSubjectId = 9 }));
    }

    [Fact]
    public void Report_engine_rechecks_permission_scope_publication_version_and_integrity()
    {
        var service = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.ExamReports.cs");
        Assert.Contains("if (!CanPublish())", service);
        Assert.Contains("CanAccessCampusAsync(scope.CampusId, ct)", service);
        Assert.Contains("GetPublicationEvidenceAsync([publication.Id]", service);
        Assert.Contains("!proof.IsReconciled", service);
        Assert.Contains("summary.ResultPublicationId == publication.Id", service);
        Assert.Contains("summary.PublicationVersionNo == publication.VersionNo", service);
        Assert.Contains("enrollment.CampusId == scope.CampusId", service);
        Assert.Contains("StudentResultCourseSnapshot", Read("EduOS.Core/Entities/Assessment/AssessmentEntities.cs"));
        Assert.Contains("immutable per-component publication evidence", service);
        Assert.Contains("!x.summary.IsPassed && !x.summary.IsWithheld", service);
        Assert.Contains(".Skip((int)skip).Take(request.PageSize)", service);
        Assert.DoesNotContain("CalculateAsync(", service);
        Assert.DoesNotContain("IgnoreQueryFilters()", service);
    }

    private static bool Valid(object value) => Validator.TryValidateObject(value,
        new ValidationContext(value), new List<ValidationResult>(), true);

    private static string Read(string relative) => File.ReadAllText(Path.Combine(
        FindRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EduOS.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("EduOS root not found.");
    }
}
