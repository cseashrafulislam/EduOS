using EduOS.App.Controllers.Api;
using EduOS.App.Middleware;
using EduOS.App.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.App;

public sealed class AuthManagementEndpointContractTests
{
    [Theory]
    [InlineData(nameof(AuthController.DisableMfa))]
    [InlineData(nameof(AuthController.RegenerateRecoveryCodes))]
    [InlineData(nameof(AuthController.RevokeSession))]
    [InlineData(nameof(AuthController.RevokeAllSessions))]
    [InlineData(nameof(AuthController.RevokeOtherSessions))]
    public void Sensitive_auth_management_actions_require_authorization_and_rate_limits(string action)
    {
        var method = typeof(AuthController).GetMethod(action)!;
        Assert.NotNull(method);
        Assert.True(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Any());
        Assert.True(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Any());
        Assert.True(typeof(AuthController).GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true).Any());
    }

    [Fact]
    public void Session_inventory_is_authenticated_and_read_only()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.GetSessions))!;
        Assert.True(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Any());
    }

    [Fact]
    public void Tenant_roles_and_session_validation_are_in_pipeline_before_authorization()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Program.cs"));
        var authenticate = source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var session = source.IndexOf("app.UseAuthSessionGuard();", StringComparison.Ordinal);
        var tenant = source.IndexOf("app.UseTenantContext();", StringComparison.Ordinal);
        var authorize = source.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        Assert.True(authenticate >= 0 && session > authenticate && tenant > session && authorize > tenant);
    }

    [Fact]
    public void Password_only_users_are_never_sent_to_mfa_setup()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        var login = source[source.IndexOf("public async Task<IActionResult> Login(", StringComparison.Ordinal)..
            source.IndexOf("// ==================== MFA Status", StringComparison.Ordinal)];
        Assert.Contains("if (user.TwoFactorEnabled)", login);
        Assert.Contains("SignInSessionAsync(user, dto.RememberMe, \"pwd\"", login);
        Assert.DoesNotContain("/Account/MfaSetup", login);
        Assert.Contains("requiresTenantSelection", login);
        Assert.Contains("dto.TenantId", login);
    }

    [Fact]
    public void Session_registry_uses_separate_identity_provider_not_jwt_refresh_table()
    {
        Assert.Equal("EduOS.CookieSession", AuthSessionRegistry.Provider);
        Assert.NotEqual("RefreshToken", AuthSessionRegistry.Provider);
    }
}
