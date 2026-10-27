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
        serviceWorker.Should().Contain("isPublicStaticResponse(request, response)");
        serviceWorker.Should().Contain("response.redirected");
        serviceWorker.Should().Contain("request.headers.has('Authorization')");
        serviceWorker.Should().Contain("private|no-store|no-cache");
        serviceWorker.Should().Contain("contentType.includes('text/css')");
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
        layout.Should().Contain("data-connectivity-notice");
        var siteScript = File.ReadAllText(Asset("site.js"));
        siteScript.Should().Contain("navigator.onLine !== false");
        siteScript.Should().Contain("addEventListener('offline'");
        siteScript.Should().Contain("addEventListener('online'");
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

    [Fact]
    public void Plan_and_payment_onboarding_are_bilingual_safe_and_server_state_driven()
    {
        var planView = File.ReadAllText(Asset("PlanSelection.cshtml"));
        var paymentView = File.ReadAllText(Asset("Payment.cshtml"));
        var planScript = File.ReadAllText(Asset("plan-selection.js"));
        var paymentScript = File.ReadAllText(Asset("payment.js"));

        new[] { planView, paymentView }.Should().AllSatisfy(view =>
        {
            view.Should().Contain("Layout = \"_OnboardingLayout\"");
            view.Should().Contain("@T[");
            view.Should().NotContain("<style>");
            view.Should().NotContain("onclick=");
        });

        new[] { planScript, paymentScript }.Should().AllSatisfy(script =>
        {
            script.Should().Contain("replaceChildren");
            script.Should().Contain("credentials: 'same-origin'");
            script.Should().NotContain(".innerHTML");
            script.Should().NotContain("Dutch Bangla");
            script.Should().NotContain("1234567890123");
        });

        planScript.Should().Contain("/api/subscription/current");
        planScript.Should().Contain("onboardingPayload.data.isComplete");
        planScript.Should().Contain("existingSubscription || !selectedPlanId");
        planScript.Should().Contain("currentPlanReadOnly");
        planScript.Should().Contain("/api/subscription/invoices/unpaid");
        // Free subscriptions may complete the payment stage, but only after
        // obtaining current server-side onboarding state and using an antiforgery token.
        planScript.Should().Contain("/api/onboarding/status");
        planScript.Should().Contain("Number(status.data?.currentStage) !== 4");
        planScript.Should().Contain("/api/onboarding/complete-step");
        planScript.Should().Contain("RequestVerificationToken");
        paymentScript.Should().Contain("manual-instructions");
        paymentScript.Should().Contain("trustedGatewayUrl");
        // Paid bank transfers must advance through the canonical server-validated stage
        // after verification; the payment screen must not bypass antiforgery or stage checks.
        paymentScript.Should().Contain("/api/onboarding/status");
        paymentScript.Should().Contain("/api/onboarding/complete-step");
        paymentScript.Should().Contain("JSON.stringify({ stage: 4, skipped: false })");
        paymentScript.Should().Contain("RequestVerificationToken");
    }

    [Fact]
    public void Module_setup_is_bilingual_safe_plan_aware_and_tenant_admin_controlled()
    {
        var view = File.ReadAllText(Asset("ModuleSetup.cshtml"));
        var script = File.ReadAllText(Asset("module-setup.js"));
        var controller = File.ReadAllText(Asset("TenantModuleController.cs"));
        var academicScript = File.ReadAllText(Asset("academic-setup.js"));

        view.Should().Contain("Layout = \"_OnboardingLayout\"");
        view.Should().Contain("@T[");
        view.Should().NotContain("<style>");
        view.Should().NotContain("onclick=");
        script.Should().Contain("/api/tenant-modules");
        script.Should().Contain("module.moduleCode");
        script.Should().Contain("module.moduleName");
        script.Should().Contain("module.isEnabled");
        script.Should().Contain("module.isEntitledByPlan");
        script.Should().Contain("module.isRequiredByPreset");
        script.Should().Contain("RequestVerificationToken");
        script.Should().NotContain("module.isSelected");
        script.Should().NotContain("module.isIncludedInPlan");
        script.Should().NotContain("module.isRequiredForInstitution");
        script.Should().Contain("rowVersion");
        script.Should().Contain("replaceChildren");
        script.Should().Contain("credentials: 'same-origin'");
        script.Should().NotContain(".innerHTML");
        script.Should().NotContain("onclick=");
        academicScript.Should().Contain("/Account/ModuleSetup");
        controller.Should().Contain("[AutoValidateAntiforgeryToken]");
        controller.Should().Contain("[Authorize(Roles = \"TenantAdmin,SuperAdmin\")]");
    }

    [Fact]
    public void Final_onboarding_views_are_bilingual_responsive_and_use_safe_dom_updates()
    {
        var brandingView = File.ReadAllText(Asset("BrandingSetup.cshtml"));
        var generalView = File.ReadAllText(Asset("GeneralSettings.cshtml"));
        var gatewayView = File.ReadAllText(Asset("GatewaySetup.cshtml"));
        var brandingScript = File.ReadAllText(Asset("branding-setup.js"));
        var generalScript = File.ReadAllText(Asset("general-settings.js"));
        var gatewayScript = File.ReadAllText(Asset("gateway-setup.js"));
        var profileController = File.ReadAllText(Asset("TenantProfileController.cs"));
        var settingController = File.ReadAllText(Asset("TenantSettingController.cs"));

        new[] { brandingView, generalView, gatewayView }.Should().AllSatisfy(view =>
        {
            view.Should().Contain("Layout = \"_OnboardingLayout\"");
            view.Should().Contain("@T[");
            view.Should().Contain("setup-page");
            view.Should().NotContain("<style>");
            view.Should().NotContain("onclick=");
        });

        new[] { brandingScript, generalScript, gatewayScript }.Should().AllSatisfy(script =>
        {
            script.Should().Contain("credentials: 'same-origin'");
            script.Should().Contain("textContent");
            script.Should().Contain("safeLocalUrl");
            script.Should().NotContain(".innerHTML");
            script.Should().NotContain("onclick=");
        });

        brandingScript.Should().Contain("replaceChildren");
        brandingScript.Should().Contain("safeAssetUrl");
        gatewayScript.Should().NotContain("/api/onboarding/complete'");
        gatewayScript.Should().Contain("/api/communication-admin/gateways");
        gatewayScript.Should().Contain("RequestVerificationToken");
        gatewayScript.Should().Contain("rowVersion");
        gatewayScript.Should().Contain("hasCredential");
        gatewayScript.Should().NotContain("/api/tenant-settings/sms-gateway");
        gatewayScript.Should().NotContain("/api/tenant-settings/email-gateway");
        profileController.Should().Contain("[AutoValidateAntiforgeryToken]");
        profileController.Should().Contain("[Authorize(Roles = \"TenantAdmin,SuperAdmin\")]");
        settingController.Should().Contain("[AutoValidateAntiforgeryToken]");
        settingController.Should().Contain("[Authorize(Roles = \"TenantAdmin,SuperAdmin\")]");
    }

    [Fact]
    public void Subscription_and_payment_writes_require_tenant_admin_and_callback_exemptions_are_explicit()
    {
        var subscriptionController = File.ReadAllText(Asset("SubscriptionController.cs"));
        var paymentController = File.ReadAllText(Asset("SubscriptionPaymentController.cs"));

        subscriptionController.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        subscriptionController.Should().Contain("[AutoValidateAntiforgeryToken]");
        paymentController.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        paymentController.Should().Contain("[Authorize(Roles = \"SuperAdmin\")]");
        paymentController.Should().Contain("[AutoValidateAntiforgeryToken]");
        paymentController.Should().Contain("[IgnoreAntiforgeryToken]");
        paymentController.Should().Contain("LocalRedirect");
        paymentController.Should().NotContain("Request.Host");
    }

    [Fact]
    public void Super_admin_bootstrap_has_no_fallback_credentials()
    {
        var seeder = File.ReadAllText(Asset("SuperAdminSeeder.cs"));

        seeder.Should().Contain("SuperAdmin:Email");
        seeder.Should().Contain("SuperAdmin:Password");
        seeder.Should().Contain("bootstrap skipped");
        seeder.Should().NotContain("superadmin@eduos.com");
        seeder.Should().NotContain("Admin@123");
        seeder.Should().NotContain("?? \"superadmin");
    }

    [Fact]
    public void Optional_mfa_flow_is_antiforgery_protected_localized_and_avoids_dom_injection()
    {
        var authController = File.ReadAllText(Asset("AuthController.cs"));
        var profileView = File.ReadAllText(Asset("Profile.cshtml"));
        var profileScript = File.ReadAllText(Asset("profile.js"));
        var program = File.ReadAllText(Asset("Program.cs"));
        var loginScript = File.ReadAllText(Asset("login.js"));
        var challengeView = File.ReadAllText(Asset("MfaChallenge.cshtml"));
        var setupView = File.ReadAllText(Asset("MfaSetup.cshtml"));
        var challengeScript = File.ReadAllText(Asset("mfa-challenge.js"));
        var setupScript = File.ReadAllText(Asset("mfa-setup.js"));

        authController.Should().Contain("[AutoValidateAntiforgeryToken]");
        authController.Should().Contain("user.TwoFactorEnabled");
        authController.Should().Contain("VerifyTwoFactorTokenAsync");
        authController.Should().Contain("RedeemTwoFactorRecoveryCodeAsync");
        authController.Should().Contain("Response.Headers.CacheControl = \"no-store\"");
        authController.Should().NotContain("Request.Headers[\"X-Forwarded-For\"]");

        program.Should().NotContain("app.UsePrivilegedMfa()");
        profileView.Should().Contain("id=\"mfaSetupLink\"");
        profileView.Should().Contain("id=\"mfaStatus\"");
        profileScript.Should().Contain("loadMfaStatus()");
        profileScript.Should().Contain("/api/auth/mfa/status");
        profileScript.Should().Contain("setupLink.classList.remove('d-none')");
        setupView.Should().Contain("href=\"/Account/Profile\"");
        loginScript.Should().Contain("sessionStorage.setItem('eduos.mfaChallenge'");

        new[] { challengeView, setupView }.Should().AllSatisfy(view =>
        {
            view.Should().Contain("Layout = \"_PublicLayout\"");
            view.Should().Contain("@T[");
            view.Should().NotContain("<style>");
            view.Should().NotContain("onclick=");
        });

        new[] { challengeScript, setupScript }.Should().AllSatisfy(script =>
        {
            script.Should().Contain("credentials: 'same-origin'");
            script.Should().Contain("textContent");
            script.Should().NotContain(".innerHTML");
            script.Should().NotContain("onclick=");
        });
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
