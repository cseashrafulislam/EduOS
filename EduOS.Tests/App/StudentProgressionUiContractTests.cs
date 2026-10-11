using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class StudentProgressionUiContractTests
{
    [Fact]
    public void Promotion_manager_uses_scoped_options_and_idempotent_concurrency_contract()
    {
        var js = File.ReadAllText(FindRepositoryFile("EduOS.App", "wwwroot", "js", "students", "progressions.js"));
        var api = File.ReadAllText(FindRepositoryFile("EduOS.App", "Controllers", "Api", "StudentPromotionsController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Students", "StudentPromotionService.cs"));
        js.Should().Contain("/promotions/options");
        js.Should().Contain("sourceEnrollmentRowVersion");
        js.Should().Contain("studentRowVersion");
        js.Should().Contain("clientRequestId: state.requestId");
        js.Should().Contain("RequestVerificationToken");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        service.Should().Contain("GetOptionsAsync");
        service.Should().Contain("x.TenantId == tenant");
        service.Should().Contain("x.CampusId == source.CampusId");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
