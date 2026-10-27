using EduOS.Service.Services.AssessmentAdministration;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentPublicationSnapshotIntegrityTests
{
    [Theory]
    [InlineData(1, 1, 1, 1, true)]
    [InlineData(50, 50, 50, 50, true)]
    [InlineData(0, 0, 0, 0, false)]
    [InlineData(2, 1, 1, 1, false)]
    [InlineData(2, 2, 1, 1, false)]
    [InlineData(2, 2, 2, 1, false)]
    public void Historical_publication_requires_complete_unique_valid_evidence(
        int total, int active, int valid, int unique, bool expected)
    {
        Assert.Equal(expected, AssessmentPublicationSnapshotIntegrity.IsReconciled(
            total, active, valid, unique));
    }

    [Fact]
    public void Integrity_query_is_scoped_and_uses_server_side_aggregates()
    {
        var source = Read("EduOS.Service", "Services", "Assessment",
            "AssessmentAdministrationService.PublicationIntegrity.cs");
        Assert.Contains("row.TenantId == tenant", source);
        Assert.Contains("enrollment.CampusId == campusId", source);
        Assert.Contains("enrollment.AcademicYearId == academicYearId", source);
        Assert.Contains("row.PublicationVersionNo == pub.VersionNo", source);
        Assert.Contains("valid.Distinct().GroupBy", source);
        Assert.Contains(".ToListAsync(ct)", source);
        Assert.DoesNotContain("IgnoreQueryFilters()", source);
    }

    private static string Read(params string[] segments)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
