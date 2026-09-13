using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public class AdmissionApplicationContractTests
{
    [Fact]
    public void Admission_api_has_role_module_antiforgery_and_rate_limit_guards()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AdmissionApplicationsController.cs"));
        source.Should().Contain("[Authorize(Roles = \"TenantAdmin,AdmissionOfficer\")]");
        source.Should().Contain("[RequireModule(\"ADMISSION\")]");
        source.Should().Contain("[AutoValidateAntiforgeryToken]");
        source.Should().Contain("[EnableRateLimiting(\"AdmissionIntakePolicy\")]");
        source.Should().Contain("[HttpPost(\"{reference:guid}/admit\")]");
        source.Should().Contain("IAdmissionEnrollmentService");
        source.Should().NotContain("IgnoreQueryFilters");

        var middleware = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "PrivilegedMfaMiddleware.cs"));
        middleware.Should().Contain("AdmissionOfficer");
    }

    [Fact]
    public void Admission_ui_is_localized_accessible_and_uses_safe_dom_rendering()
    {
        var view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AdmissionsIndex.cshtml"));
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "admissions-index.js"));

        view.Should().Contain("@T[\"AdmissionApplications\"]");
        view.Should().Contain("aria-live=\"polite\"");
        view.Should().Contain("autocomplete=\"tel\"");
        view.Should().NotContain("onclick=");
        view.Should().NotContain("style=\"");
        script.Should().Contain("clientRequestId");
        script.Should().Contain("/admit");
        script.Should().Contain("credentials: 'same-origin'");
        script.Should().Contain("textContent");
        script.Should().NotContain("innerHTML");
        script.Should().NotContain("localStorage");
    }
}
