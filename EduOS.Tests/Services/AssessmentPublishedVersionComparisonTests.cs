using System.ComponentModel.DataAnnotations;
using EduOS.Core.DTOs.Assessment;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentPublishedVersionComparisonTests
{
    [Fact]
    public void Comparison_requests_are_bounded_and_require_valid_publication_ids()
    {
        Assert.False(Valid(new PublishedVersionComparisonQueryDto { PublicationId = 0 }));
        Assert.False(Valid(new PublishedVersionComparisonQueryDto { PublicationId = 1, Page = 0 }));
        Assert.False(Valid(new PublishedVersionComparisonQueryDto { PublicationId = 1, PageSize = 51 }));
        Assert.False(Valid(new PublishedVersionComparisonQueryDto { PublicationId = 1, BaselinePublicationId = 0 }));
        Assert.False(Valid(new PublishedVersionComparisonQueryDto { PublicationId = 1, Search = new string('a', 101) }));
        Assert.True(Valid(new PublishedVersionComparisonQueryDto { PublicationId = 1, PageSize = 50 }));
    }

    [Fact]
    public void Comparison_enforces_tenant_campus_provenance_and_sql_paging_without_mutation()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.PublicationComparison.cs");
        Assert.Contains("if (!CanPublish())", source);
        Assert.Contains("x.TenantId == tenant && x.Id == request.PublicationId", source);
        Assert.Contains("x.TenantId == tenant && x.AssessmentId == target.AssessmentId", source);
        Assert.Contains("x.AcademicBatchId == target.AcademicBatchId", source);
        Assert.Contains("x.VersionNo < target.VersionNo", source);
        Assert.Contains("CanAccessCampusAsync(scope.CampusId, ct)", source);
        Assert.Contains("GetPublicationEvidenceAsync([baseline.Id, target.Id]", source);
        Assert.Contains("validCount != expectedCount", source);
        Assert.Contains("enrollment.CampusId == scope.CampusId", source);
        Assert.Contains("enrollment.AcademicYearId == scope.AcademicYearId", source);
        Assert.Contains(".Union(afterRows.Select(x => x.StudentEnrollmentId))", source);
        Assert.Contains(".Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)", source);
        Assert.Contains("x.prior.IsWithheld != x.current.IsWithheld", source);
        Assert.DoesNotContain("IgnoreQueryFilters()", source);
        Assert.DoesNotContain("_summaries.Update(", source);
        Assert.DoesNotContain("_summaries.AddAsync(", source);
        Assert.DoesNotContain("CalculateAsync(", source);
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
