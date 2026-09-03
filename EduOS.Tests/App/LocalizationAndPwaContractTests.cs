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
