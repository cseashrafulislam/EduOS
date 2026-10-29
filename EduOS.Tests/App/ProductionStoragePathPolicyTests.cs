using EduOS.App.Security;
using Xunit;

namespace EduOS.Tests.App;

public sealed class ProductionStoragePathPolicyTests
{
    [Theory]
    [InlineData("wwwroot/uploads", "App_Data/keys", "FileStorage:PrivateBasePath")]
    [InlineData("wwwroot", "App_Data/keys", "FileStorage:PrivateBasePath")]
    [InlineData("wwwroot/uploads/../documents", "App_Data/keys", "FileStorage:PrivateBasePath")]
    [InlineData("App_Data/private", "wwwroot/keys", "DataProtection:KeysPath")]
    [InlineData("App_Data/private", "wwwroot/keys/../other", "DataProtection:KeysPath")]
    public void Publicly_served_private_directories_are_rejected(string privateDir,
        string keyDir, string invalidSetting)
    {
        var root = Path.Combine(Path.GetTempPath(), "eduos-private-path-policy");
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProductionStoragePathPolicy.Validate(root, Path.Combine(root, "wwwroot"), privateDir, keyDir));
        Assert.Contains(invalidSetting, error.Message);
    }

    [Fact]
    public void Private_directories_outside_web_root_are_allowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "eduos-private-path-policy");
        ProductionStoragePathPolicy.Validate(root, Path.Combine(root, "wwwroot"),
            "App_Data/private-uploads", "App_Data/keys");
    }

    [Fact]
    public void Empty_private_directory_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "eduos-private-path-policy");
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProductionStoragePathPolicy.Validate(root, Path.Combine(root, "wwwroot"),
                "", "App_Data/keys"));
        Assert.Contains("FileStorage:PrivateBasePath", error.Message);
    }
}
