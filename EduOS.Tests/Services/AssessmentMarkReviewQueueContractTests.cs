using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentMarkReviewQueueContractTests
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
    public void Reviewer_queue_is_tenant_campus_and_role_scoped_without_student_data()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkReviewQueue.cs");
        source.Should().Contain("if (!CanPublish())");
        source.Should().Contain("exam.TenantId == tenant && sub.TenantId == tenant");
        source.Should().Contain("IsRestrictedCampusManager()");
        source.Should().Contain("AllowedCampusIds(tenant).Contains(x.exam.CampusId)");
        source.Should().Contain("from review in latest.Where(x => x.AssessmentSubjectId == sub.Id).DefaultIfEmpty()");
        source.Should().Contain("OrderByDescending(x => x.exam.StartDate)");
        source.Should().Contain(".Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)");
        source.Should().Contain("x.Actor != _user.UserId && current");
        source.Should().NotContain("_students.GetQueryable()");
        source.Should().NotContain("IgnoreQueryFilters()");
    }

    [Fact]
    public void Queue_is_connected_to_authorized_api_and_existing_review_workspace()
    {
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        var view = Read("EduOS.App/Views/AssessmentAdministration/MarkReviews.cshtml");
        var contract = Read("EduOS.Core/Interfaces/IServices/IAssessmentAdministrationService.cs");
        api.Should().Contain("[HttpGet(\"marks/reviews/queue\")]");
        api.Should().Contain("SearchMarkReviewQueueAsync(query, ct)");
        contract.Should().Contain("SearchMarkReviewQueueAsync(AssessmentMarkReviewQueueQueryDto");
        view.Should().Contain("id=\"markReviewQueueRows\"");
        view.Should().Contain("id=\"markReviewQueueStatus\"");
    }
}
