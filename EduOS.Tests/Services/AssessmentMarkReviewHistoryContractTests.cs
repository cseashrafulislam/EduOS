using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentMarkReviewHistoryContractTests
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
    public void History_reuses_subject_campus_assignment_authorization_and_bounded_keyset_sql()
    {
        var source = Read("EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkReviews.cs");
        source.Should().Contain("await GetMarkReviewAsync(subjectId, ct)");
        source.Should().Contain("x.TenantId == tenant && x.AssessmentSubjectId == subjectId && !x.IsDeleted");
        source.Should().Contain("x.Id < request.BeforeId.Value");
        source.Should().Contain("x.Decision == request.Decision.Value");
        source.Should().Contain("OrderByDescending(x => x.Id).Take(request.PageSize + 1)");
        source.Should().Contain("NextBeforeId = hasMore");
        source.Should().NotContain("IgnoreQueryFilters()");
    }

    [Fact]
    public void Authorized_api_and_ui_expose_stable_read_only_history_pages()
    {
        var api = Read("EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs");
        var contract = Read("EduOS.Core/Interfaces/IServices/IAssessmentAdministrationService.cs");
        var view = Read("EduOS.App/Views/AssessmentAdministration/MarkReviews.cshtml");
        api.Should().Contain("[HttpGet(\"marks/reviews/{subjectId:long}/history\")]");
        contract.Should().Contain("SearchMarkReviewHistoryAsync(");
        view.Should().Contain("id=\"markReviewHistoryDecision\"");
        view.Should().Contain("id=\"markReviewHistoryNext\"");
    }
}
