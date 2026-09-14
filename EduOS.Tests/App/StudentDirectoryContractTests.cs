using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public class StudentDirectoryContractTests
{
    [Fact]
    public void Student_directory_api_and_page_have_role_module_and_cache_guards()
    {
        var api = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "StudentsApiController.cs"));
        var page = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "StudentsController.cs"));
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin,AdmissionOfficer\")]");
        api.Should().Contain("[RequireModule(\"STUDENT\")]");
        api.Should().Contain("[AutoValidateAntiforgeryToken]");
        api.Should().Contain("ResponseCache(NoStore = true");
        api.Should().NotContain("IgnoreQueryFilters");
        page.Should().Contain("ResponseCache(NoStore = true");
    }

    [Fact]
    public void Student_directory_ui_is_localized_accessible_and_uses_safe_dom()
    {
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "StudentsIndex.cshtml"));
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "students-index.js"));
        view.Should().Contain("@T[\"StudentDirectory\"]");
        view.Should().Contain("aria-live=\"polite\"");
        view.Should().NotContain("onclick=");
        script.Should().Contain("credentials: 'same-origin'");
        script.Should().Contain("textContent");
        script.Should().NotContain("innerHTML");
        script.Should().NotContain("localStorage");
    }
}
