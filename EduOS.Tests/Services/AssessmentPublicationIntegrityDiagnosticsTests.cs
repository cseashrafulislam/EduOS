using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentPublicationIntegrityDiagnosticsTests
{
    [Fact]
    public void Integrity_query_requires_bounded_pages()
    {
        Assert.False(Valid(new PublishedSnapshotIntegrityQueryDto { Page = 0 }));
        Assert.False(Valid(new PublishedSnapshotIntegrityQueryDto { PageSize = 0 }));
        Assert.False(Valid(new PublishedSnapshotIntegrityQueryDto { PageSize = 51 }));
        Assert.True(Valid(new PublishedSnapshotIntegrityQueryDto { Page = 1, PageSize = 50 }));
    }

    [Fact]
    public void Diagnostics_are_tenant_campus_authorized_and_do_not_rewrite_history()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.PublicationIntegrityIssues.cs");
        Assert.Contains("if (!CanPublish())", source);
        Assert.Contains("x.TenantId == tenant && x.Id == publicationId", source);
        Assert.Contains("CanAccessCampusAsync(scope.CampusId, ct)", source);
        Assert.Contains("row.TenantId, Id = row.StudentEnrollmentId", source);
        Assert.Contains("enrollment.TenantId, enrollment.Id", source);
        Assert.Contains("enrollment.CampusId == scope.CampusId", source);
        Assert.Contains("enrollment.AcademicYearId == scope.AcademicYearId", source);
        Assert.Contains("duplicates.Contains(row.StudentEnrollmentId)", source);
        Assert.Contains("!validIds.Contains(row.Id)", source);
        Assert.Contains(".Skip(skip).Take(request.PageSize)", source);
        Assert.DoesNotContain("_summaries.Update(", source);
        Assert.DoesNotContain("_summaries.AddAsync(", source);
        Assert.DoesNotContain("IgnoreQueryFilters()", source);
    }

    private static bool Valid(object request) =>
        Validator.TryValidateObject(request, new ValidationContext(request),
            new List<ValidationResult>(), validateAllProperties: true);

    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }
}
