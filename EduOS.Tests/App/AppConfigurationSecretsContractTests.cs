using System.Text.Json;
using Xunit;

namespace EduOS.Tests.App;

public sealed class AppConfigurationSecretsContractTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

    [Fact]
    public void Checked_in_settings_must_not_contain_bootstrap_or_smtp_credentials()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Asset("appsettings.json")));
        var root = json.RootElement;
        var admin = root.GetProperty("SuperAdmin");
        var email = root.GetProperty("EmailSettings");

        Assert.Equal(string.Empty, admin.GetProperty("Email").GetString());
        Assert.Equal(string.Empty, admin.GetProperty("Password").GetString());
        Assert.Equal(string.Empty, email.GetProperty("SenderEmail").GetString());
        Assert.Equal(string.Empty, email.GetProperty("Password").GetString());
    }

    [Fact]
    public void Super_admin_bootstrap_must_use_configuration_instead_of_hardcoded_credentials()
    {
        var seeder = File.ReadAllText(Asset("SuperAdminSeeder.cs"));
        Assert.Contains("configuration[\"SuperAdmin:Email\"]", seeder);
        Assert.Contains("configuration[\"SuperAdmin:Password\"]", seeder);
        Assert.Contains("bootstrap skipped because SuperAdmin:Email is not configured", seeder);
    }
}
