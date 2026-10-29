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
    [InlineData(nameof(AuthController.SwitchInstitution))]
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
    public void Session_context_is_authenticated_and_read_only()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.CurrentSessionContext))!;
        Assert.True(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Any());
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthInstitutionSwitchController.cs"));
        Assert.Contains("session-context", source);
        Assert.Contains("AuthSessionRegistry.ToPublicReference(sid)", source);
        Assert.Contains("CacheControl = \"no-store\"", source);
    }

    [Fact]
    public void Institution_choices_are_authenticated_and_read_only()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.GetAvailableInstitutions))!;
        Assert.True(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Any());
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthInstitutionSwitchController.cs"));
        var choices = source[source.IndexOf("public async Task<IActionResult> GetAvailableInstitutions()", StringComparison.Ordinal)..
            source.IndexOf("// This read-only, no-store snapshot", StringComparison.Ordinal)];
        Assert.Equal(2, choices.Split("BindAccountReadbackSession();").Length - 1);
        Assert.Equal(2, choices.Split("AuthSessionRegistry.IsValidPrincipalAsync(_db, User)").Length - 1);
    }

    [Fact]
    public void Institution_switch_requires_current_session_membership_and_mfa_before_reissuing_cookie()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthInstitutionSwitchController.cs"));
        Assert.Contains("AuthSessionRegistry.IsValidAsync", source);
        Assert.Contains("GetSessionContextAsync(user, request.TenantId)", source);
        Assert.Contains("selected.TenantId != request.TenantId", source);
        Assert.Contains("user.TwoFactorEnabled && !User.HasClaim", source);
        Assert.Contains("AuthSessionRegistry.RotateAsync", source);
        Assert.Contains("catch (DbUpdateConcurrencyException)", source);
        Assert.Contains("selected, rotatedSid", source);
        Assert.Contains("SignInSessionAsync(user", source);
        Assert.Contains("Clear-Site-Data", source);
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
    public void Individual_session_revoke_uses_public_reference_and_password_step_up()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        var start = source.IndexOf("public async Task<IActionResult> RevokeSession(", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IActionResult> RevokeOtherSessions(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var action = source[start..end];
        Assert.Contains("AuthSessionRegistry.IsCurrentReference", action);
        Assert.Contains("CheckPasswordAsync(user, dto.CurrentPassword)", action);
        Assert.Contains("user.TwoFactorEnabled && !User.HasClaim", action);
        Assert.Contains("AuthSessionRegistry.RevokeByReferenceForCurrentSessionAsync", action);
        Assert.Contains("HttpContext.RequestAborted", action);
        Assert.Contains("if (!revoked.HasValue)", action);
        Assert.DoesNotContain("revoked session {SessionId}", action);
    }

    [Fact]
    public void Mfa_management_secret_changes_and_session_revocation_share_one_transaction()
    {
        var auth = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        foreach (var (start, end, lastWrite) in new[]
        {
            ("public async Task<IActionResult> SetupMfa(", "// ==================== MFA Enable", "return key;"),
            ("public async Task<IActionResult> DisableMfa(", "public async Task<IActionResult> RegenerateRecoveryCodes(", "return true;"),
            ("public async Task<IActionResult> RegenerateRecoveryCodes(", "private async Task<bool> VerifyMfaActionCodeAsync", "return newCodes;")
        })
        {
            var from = auth.IndexOf(start, StringComparison.Ordinal);
            var to = auth.IndexOf(end, from, StringComparison.Ordinal);
            Assert.True(from >= 0 && to > from);
            var action = auth[from..to];
            var transaction = action.IndexOf("_db.ExecuteInTransactionAsync(", StringComparison.Ordinal);
            var revoke = action.IndexOf("await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);", StringComparison.Ordinal);
            var endOfTransaction = action.IndexOf(lastWrite, StringComparison.Ordinal);
            Assert.True(transaction >= 0 && revoke > transaction && endOfTransaction > revoke);
            Assert.Equal(1, action.Split("await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);").Length - 1);
        }
    }

    [Fact]
    public void Antiforgery_refresh_is_authenticated_and_uses_the_current_cookie_session()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.RefreshAntiforgery))!;
        Assert.NotNull(method);
        Assert.True(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(method.GetCustomAttributes(typeof(HttpGetAttribute), true).Any());
        var auth = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        Assert.Contains("antiforgery.GetAndStoreTokens(HttpContext)", auth);
        Assert.Contains("AuthSessionRegistry.ToPublicReference(sid)", auth);
        Assert.Contains("Response.Headers.CacheControl = \"no-store\"", auth);
    }

    [Fact]
    public void Session_registry_uses_separate_identity_provider_not_jwt_refresh_table()
    {
        Assert.Equal("EduOS.CookieSession", AuthSessionRegistry.Provider);
        Assert.NotEqual("RefreshToken", AuthSessionRegistry.Provider);
    }
    [Theory]
    [InlineData(nameof(AuthController.SetupMfa), "// ==================== MFA Enable")]
    [InlineData(nameof(AuthController.EnableMfa), "// ==================== MFA Login")]
    [InlineData(nameof(AuthController.DisableMfa), "public async Task<IActionResult> RegenerateRecoveryCodes(")]
    [InlineData(nameof(AuthController.RegenerateRecoveryCodes), "private async Task<bool> VerifyMfaActionCodeAsync")]
    public void Mfa_credential_mutations_recheck_exact_session_in_serializable_transaction(
        string method, string next)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        var start = source.IndexOf("public async Task<IActionResult> " + method + "(", StringComparison.Ordinal);
        var end = source.IndexOf(next, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var action = source[start..end];
        Assert.Contains("AuthSessionRegistry.IsValidPrincipalAsync(_db, User)", action);
        Assert.Contains("throw new AccountSessionChangedException();", action);
        Assert.Contains("System.Data.IsolationLevel.Serializable, HttpContext.RequestAborted", action);
        Assert.Contains("catch (AccountSessionChangedException)", action);
    }

    [Theory]
    [InlineData(nameof(AuthController.DisableMfa), "public async Task<IActionResult> RegenerateRecoveryCodes(")]
    [InlineData(nameof(AuthController.RegenerateRecoveryCodes), "private async Task<bool> VerifyMfaActionCodeAsync")]
    public void Mfa_management_response_is_bound_to_original_session_before_signout(
        string method, string next)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AuthController.cs"));
        var start = source.IndexOf("public async Task<IActionResult> " + method + "(", StringComparison.Ordinal);
        var end = source.IndexOf(next, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var action = source[start..end];
        var binding = action.IndexOf("BindAccountReadbackSession();", StringComparison.Ordinal);
        var signout = action.IndexOf("await _signInManager.SignOutAsync();", StringComparison.Ordinal);
        Assert.True(binding >= 0 && signout > binding);
    }
}
