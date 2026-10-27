using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentPublishedReadbackIntegrityContractTests
{
    [Fact]
    public void Published_readback_checks_entire_version_before_returning_student_data()
    {
        var source = ReadSource();
        var start = source.IndexOf("if (publishedOnly)", StringComparison.Ordinal);
        var end = source.IndexOf("var calculated = await CalculateAsync(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var readback = source[start..end];
        Assert.Contains("snapshotCount == 0 || snapshotCount > 5000", readback);
        Assert.Contains("GroupBy(x => x.StudentEnrollmentId)", readback);
        Assert.Contains("group.Count() > 1", readback);
        Assert.Contains("snapshot.Count != snapshotCount", readback);
        Assert.Contains("return Error<AssessmentResultSheetDto>", readback);
    }

    [Fact]
    public void Published_readback_rejects_wrong_campus_version_and_invalid_snapshot_values()
    {
        var source = ReadSource();
        var start = source.IndexOf("if (publishedOnly)", StringComparison.Ordinal);
        var end = source.IndexOf("var calculated = await CalculateAsync(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var readback = source[start..end];
        Assert.Contains("row.TenantId == tenant", readback);
        Assert.Contains("enrollment.TenantId == tenant", readback);
        Assert.Contains("student.TenantId == tenant", readback);
        Assert.Contains("row.AssessmentId == publication.AssessmentId", readback);
        Assert.Contains("row.PublicationVersionNo == publication.VersionNo", readback);
        Assert.Contains("enrollment.AcademicBatchId == publication.AcademicBatchId", readback);
        Assert.Contains("enrollment.CampusId == scope.Value.Exam.CampusId", readback);
        Assert.Contains("enrollment.AcademicYearId == scope.Value.Exam.AcademicYearId", readback);
        Assert.Contains("row.ObtainedMarks <= row.TotalMarks", readback);
        Assert.Contains("!row.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted", readback);
    }

    private static string ReadSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "EduOS.Service", "Services", "Assessment",
                "AssessmentAdministrationService.cs");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("Assessment service source not found.");
    }
}
