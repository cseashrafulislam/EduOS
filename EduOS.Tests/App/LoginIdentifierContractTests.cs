using System.Text.Json;
using EduOS.Core.DTOs.Auth;
using Xunit;

namespace EduOS.Tests.App;

public sealed class LoginIdentifierContractTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

    [Fact]
    public void Login_request_uses_username_or_email_not_a_misleading_email_field()
    {
        var request = new LoginRequestDto { UserNameOrEmail = "teacher01", Password = "password" };
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("teacher01", document.RootElement.GetProperty("userNameOrEmail").GetString());
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.Null(typeof(LoginRequestDto).GetProperty("Email"));
    }

    [Fact]
    public void Login_controller_view_and_browser_submit_the_same_identifier_contract()
    {
        var controller = File.ReadAllText(Asset("AuthController.cs"));
        var view = File.ReadAllText(Asset("Login.cshtml"));
        var javascript = File.ReadAllText(Asset("login.js"));
        var login = controller[controller.IndexOf("public async Task<IActionResult> Login(", StringComparison.Ordinal)
            ..controller.IndexOf("// ==================== MFA Status", StringComparison.Ordinal)];

        Assert.Contains("dto.UserNameOrEmail.Trim()", login);
        Assert.DoesNotContain("dto.Email", login);
        Assert.Contains("id=\"userNameOrEmail\"", view);
        Assert.Contains("document.getElementById('userNameOrEmail')", javascript);
        Assert.Contains("JSON.stringify({ userNameOrEmail, password, rememberMe, tenantId })", javascript);
    }

    [Fact]
    public void Dead_token_login_and_mfa_abstractions_are_not_reintroduced()
    {
        var assembly = typeof(LoginRequestDto).Assembly;
        foreach (var oldType in new[]
        {
            "EduOS.Core.Interfaces.IServices.IAuthenticationService",
            "EduOS.Core.Interfaces.IServices.IMfaEnrollmentService",
            "EduOS.Core.DTOs.Auth.LoginResponseDto",
            "EduOS.Core.DTOs.Auth.RefreshTokenRequestDto",
            "EduOS.Core.DTOs.Auth.LogoutRequestDto",
            "EduOS.Core.DTOs.Auth.MfaEnrollmentStartDto",
            "EduOS.Core.DTOs.Auth.MfaRecoveryCodesDto"
        })
            Assert.Null(assembly.GetType(oldType));

        Assert.NotNull(assembly.GetType("EduOS.Core.DTOs.Auth.MfaSecurityActionDto"));
        Assert.NotNull(assembly.GetType("EduOS.Core.DTOs.Auth.RevokeUserSessionDto"));
    }
}
