using FluentAssertions;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace EduOS.Tests.App;

public class LocalizationAndPwaContractTests
{
    private static readonly string AssetDirectory =
        Path.Combine(AppContext.BaseDirectory, "TestAssets");

    [Fact]
    public void English_and_Bangla_resources_have_matching_contracts()
    {
        var english = ReadResource("SharedResource.resx");
        var bangla = ReadResource("SharedResource.bn-BD.resx");

        bangla.Keys.Should().BeEquivalentTo(english.Keys);
        english.Keys.Should().Contain(
        new[]
        {
            "Dashboard",
            "Language",
            "SetupWizard",
            "OfflineMessage",
            "Onboarding_EmailVerification_Name",
            "Onboarding_GatewaySetup_Description"
        });
        bangla["Dashboard"].Should().NotBe(english["Dashboard"]);
    }

    [Fact]
    public void Pwa_cache_contract_excludes_tenant_data_and_authenticated_pages()
    {
        var serviceWorker = File.ReadAllText(Asset("service-worker.js"));
        var coreAssetsEnd = serviceWorker.IndexOf(
            "self.addEventListener('install'", StringComparison.Ordinal);
        coreAssetsEnd.Should().BeGreaterThan(0);
        var coreAssets = serviceWorker[..coreAssetsEnd];

        coreAssets.Should().NotContain("/api/");
        coreAssets.Should().NotContain("/uploads/");
        coreAssets.Should().NotContain("/hangfire");
        serviceWorker.Should().Contain("request.mode === 'navigate'");
        serviceWorker.Should().Contain("cache: 'no-store'");
        serviceWorker.Should().Contain("url.origin !== self.location.origin");
    }

    [Fact]
    public void Shared_shell_uses_local_assets_and_manifest_is_installable()
    {
        var layout = File.ReadAllText(Asset("_Layout.cshtml"));
        var onboardingLayout = File.ReadAllText(Asset("_OnboardingLayout.cshtml"));
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Asset("manifest.webmanifest")));

        layout.Should().NotContain("https://");
        onboardingLayout.Should().NotContain("https://");
        layout.Should().Contain("~/manifest.webmanifest");
        layout.Should().Contain("data-language-selector");
        manifest.RootElement.GetProperty("display").GetString().Should().Be("standalone");
        manifest.RootElement.GetProperty("icons").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Public_authentication_shell_is_localized_and_avoids_html_injection_sinks()
    {
        var publicLayout = File.ReadAllText(Asset("_PublicLayout.cshtml"));
        var loginView = File.ReadAllText(Asset("Login.cshtml"));
        var signupView = File.ReadAllText(Asset("Signup.cshtml"));
        var loginScript = File.ReadAllText(Asset("login.js"));
        var signupScript = File.ReadAllText(Asset("signup.js"));
        var authScript = File.ReadAllText(Asset("auth-forms.js"));

        publicLayout.Should().NotContain("https://");
        publicLayout.Should().Contain("data-language-selector");
        loginView.Should().Contain("@T[");
        signupView.Should().Contain("@T[");
        signupScript.Should().Contain("/api/platform-catalog/institution-types");
        loginScript.Should().Contain("candidate.startsWith('//')");
        new[] { loginScript, signupScript, authScript }
            .Should().AllSatisfy(script => script.Should().NotContain(".innerHTML"));
    }

    [Fact]
    public void Pricing_view_uses_bilingual_catalog_fields_and_safe_dom_rendering()
    {
        var pricing = File.ReadAllText(Asset("Pricing.cshtml"));

        pricing.Should().Contain("nameBangla");
        pricing.Should().Contain("featureNameBangla");
        pricing.Should().Contain("toLocaleString(i18n.culture)");
        pricing.Should().Contain("encodeURIComponent(plan.code");
        pricing.Should().NotContain(".innerHTML");
        pricing.Should().NotContain("https://");
    }

    [Fact]
    public void Tenant_dashboard_is_localized_responsive_and_uses_safe_local_alert_actions()
    {
        var dashboard = File.ReadAllText(Asset("Dashboard.cshtml"));

        dashboard.Should().Contain("@T[");
        dashboard.Should().Contain("SafeLocalUrl");
        dashboard.Should().Contain("<progress");
        dashboard.Should().Contain("asp-controller=\"Account\"");
        dashboard.Should().NotContain("<style>");
        dashboard.Should().NotContain("/UserManagement");
        dashboard.Should().NotContain("Monthly Collection");
    }

    [Fact]
    public void Super_admin_dashboard_is_role_restricted_and_contains_no_fake_metrics()
    {
        var dashboard = File.ReadAllText(Asset("AdminDashboard.cshtml"));
        var controller = File.ReadAllText(Asset("DashboardController.cs"));

        dashboard.Should().Contain("@T[");
        dashboard.Should().NotContain("Total Users");
        dashboard.Should().NotContain("Total Tenants");
        controller.Should().Contain("[Authorize(Roles = \"SuperAdmin\")]");
    }

    [Fact]
    public void Core_institution_setup_is_bilingual_responsive_and_uses_safe_dom_rendering()
    {
        var profileView = File.ReadAllText(Asset("InstitutionProfile.cshtml"));
        var campusView = File.ReadAllText(Asset("CampusSetup.cshtml"));
        var academicView = File.ReadAllText(Asset("AcademicSetup.cshtml"));
        var profileScript = File.ReadAllText(Asset("institution-profile.js"));
        var campusScript = File.ReadAllText(Asset("campus-setup.js"));
        var academicScript = File.ReadAllText(Asset("academic-setup.js"));

        new[] { profileView, campusView, academicView }.Should().AllSatisfy(view =>
        {
            view.Should().Contain("Layout = \"_OnboardingLayout\"");
            view.Should().Contain("@T[");
            view.Should().Contain("setup-page");
            view.Should().NotContain("onclick=");
        });

        new[] { profileScript, campusScript, academicScript }.Should().AllSatisfy(script =>
        {
            script.Should().Contain("replaceChildren");
            script.Should().NotContain(".innerHTML");
            script.Should().NotContain("onclick=");
            script.Should().Contain("credentials: 'same-origin'");
        });

        profileScript.Should().Contain("/api/platform-catalog/institution-types");
        campusScript.Should().Contain("addEventListener('click', handleListAction)");
        academicScript.Should().Contain("addEventListener('click', handleListAction)");
    }

    [Fact]
    public void Onboarding_writes_are_tenant_admin_only_and_receive_anti_forgery_tokens()
    {
        var layout = File.ReadAllText(Asset("_Layout.cshtml"));
        var publicLayout = File.ReadAllText(Asset("_PublicLayout.cshtml"));
        var siteScript = File.ReadAllText(Asset("site.js"));
        var accountController = File.ReadAllText(Asset("AccountController.cs"));
        var institutionController = File.ReadAllText(Asset("InstitutionOnboardingController.cs"));
        var onboardingController = File.ReadAllText(Asset("OnboardingController.cs"));

        layout.Should().Contain("request-verification-token");
        publicLayout.Should().Contain("request-verification-token");
        siteScript.Should().Contain("RequestVerificationToken");
        siteScript.Should().Contain("url.origin !== window.location.origin");
        accountController.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        institutionController.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        institutionController.Should().Contain("[AutoValidateAntiforgeryToken]");
        onboardingController.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        onboardingController.Should().Contain("[AutoValidateAntiforgeryToken]");
    }

    private static Dictionary<string, string> ReadResource(string fileName)
    {
        return XDocument.Load(Asset(fileName))
            .Root!
            .Elements("data")
            .ToDictionary(
                node => node.Attribute("name")!.Value,
                node => node.Element("value")!.Value,
                StringComparer.Ordinal);
    }

    private static string Asset(string fileName) =>
        Path.Combine(AssetDirectory, fileName);
}
