using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.App;

public sealed class OptionalMfaAuthContractTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

    [Fact]
    public void Password_login_never_requires_mfa_enrollment_before_dashboard()
    {
        var api = File.ReadAllText(Asset("AuthController.cs"));
        var program = File.ReadAllText(Asset("Program.cs"));
        var login = api[api.IndexOf("public async Task<IActionResult> Login(",
            StringComparison.Ordinal)..api.IndexOf("// ==================== MFA Status", StringComparison.Ordinal)];
        Assert.Contains("if (!user.IsActive || user.IsDeleted)", login);
        Assert.Contains("if (!user.EmailConfirmed)", login);
        Assert.Contains("if (user.TwoFactorEnabled)", login);
        Assert.Contains("SignInSessionAsync(user, dto.RememberMe, \"pwd\"", login);
        Assert.Contains("requiresTwoFactor = false", login);
        Assert.Contains("GetRedirectUrl(session.Role, session.TenantId)", login);
        Assert.DoesNotContain("/Account/MfaSetup", login);
        Assert.DoesNotContain("app.UsePrivilegedMfa()", program);
    }

    [Fact]
    public void Mfa_is_an_authenticated_voluntary_profile_action()
    {
        var profile = File.ReadAllText(Asset("Profile.cshtml"));
        var profileJs = File.ReadAllText(Asset("profile.js"));
        var setup = File.ReadAllText(Asset("MfaSetup.cshtml"));
        var loginView = File.ReadAllText(Asset("Login.cshtml"));
        Assert.Contains("id=\"passwordForm\"", profile);
        Assert.Contains("id=\"mfaStatus\"", profile);
        Assert.Contains("id=\"mfaSetupLink\"", profile);
        Assert.Contains("href=\"/Account/MfaSetup\"", profile);
        Assert.Contains("loadMfaStatus()", profileJs);
        Assert.Contains("/api/auth/mfa/status", profileJs);
        Assert.Contains("if (response.data.enabled)", profileJs);
        Assert.Contains("setupLink.classList.remove('d-none')", profileJs);
        Assert.Contains("href=\"/Account/Profile\"", setup);
        Assert.Contains("type=\"text\" id=\"userNameOrEmail\"", loginView);
        Assert.Contains("@T[\"UsernameOrEmail\"]", loginView);
        Assert.True(typeof(AccountController).GetMethod(nameof(AccountController.MfaSetup))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
    }

    [Fact]
    public void Auth_api_requires_correct_session_and_protects_mfa_mutations()
    {
        var controller = typeof(AuthController);
        Assert.True(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true).Any());
        Assert.True(controller.GetMethod(nameof(AuthController.Login))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any());
        Assert.True(controller.GetMethod(nameof(AuthController.MfaStatus))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(controller.GetMethod(nameof(AuthController.SetupMfa))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(controller.GetMethod(nameof(AuthController.EnableMfa))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        Assert.True(controller.GetMethod(nameof(AuthController.CompleteMfaLogin))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any());

        var api = File.ReadAllText(Asset("AuthController.cs"));
        Assert.Contains("_userManager.CheckPasswordAsync(user, dto.CurrentPassword)", api);
        Assert.Contains("_userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code)", api);
        Assert.Contains("_userManager.SetTwoFactorEnabledAsync(user, true)", api);
        Assert.Contains("_mfaChallengeService.TryRead(dto.ChallengeToken, out var challenge)", api);
        Assert.Contains("FixedTimeEquals(challenge.SecurityStamp, currentStamp)", api);
        Assert.Contains("user.IsDeleted", api);
        Assert.Contains("tenant.OnboardingStage != OnboardingStage.Completed", api);
    }

    [Fact]
    public void Browser_cookie_post_requests_are_antiforgery_protected()
    {
        var siteJs = File.ReadAllText(Asset("site.js"));
        var layout = File.ReadAllText(Asset("_PublicLayout.cshtml"));
        var profileJs = File.ReadAllText(Asset("profile.js"));
        Assert.Contains("request-verification-token", layout);
        Assert.Contains("new Request(request, { headers })", siteJs);
        Assert.Contains("headers.set('RequestVerificationToken', verificationToken)", siteJs);
        Assert.Contains("getAntiForgeryToken()", profileJs);
    }

    [Fact]
    public void Mfa_challenge_consumption_and_session_revocation_are_enforced()
    {
        var auth = File.ReadAllText(Asset("AuthController.cs"));
        var identity = File.ReadAllText(Asset("IdentityExtensions.cs"));
        var tenant = File.ReadAllText(Asset("TenantContextMiddleware.cs"));
        var profile = File.ReadAllText(Asset("profile.js"));
        Assert.Contains("_userManager.UpdateSecurityStampAsync(user)", auth);
        Assert.Contains("if (!consumed.Succeeded)", auth);
        Assert.Contains("services.Configure<SecurityStampValidatorOptions>", identity);
        Assert.Contains("options.ValidationInterval = TimeSpan.Zero", identity);
        Assert.Contains("if (!user.IsActive || user.IsDeleted || !user.EmailConfirmed)", tenant);
        Assert.Contains("window.location.assign('/Account/Login?status=password-changed')", profile);
    }

    [Fact]
    public void Profile_and_address_changes_commit_in_one_transaction()
    {
        var auth = File.ReadAllText(Asset("AuthController.cs"));
        Assert.Contains("_db.ExecuteInTransactionAsync", auth);
        Assert.Contains("throw new AuthTransactionRejectedException(", auth);
    }

    [Fact]
    public void Campus_login_rate_limit_is_separate_from_per_user_mfa_enrollment()
    {
        var rateLimiter = File.ReadAllText(Asset("RateLimiterExtensions.cs"));
        var auth = File.ReadAllText(Asset("AuthController.cs"));
        Assert.Contains("options.AddPolicy(\"LoginPolicy\"", rateLimiter);
        Assert.Contains("options.AddPolicy(\"MfaPolicy\"", rateLimiter);
        Assert.Contains("options.AddPolicy(\"MfaLoginPolicy\"", rateLimiter);
        Assert.Contains("GetClientPartition(context)", rateLimiter);
        Assert.Contains("GetIpPartition(context)", rateLimiter);
        Assert.Contains("[EnableRateLimiting(\"MfaLoginPolicy\")]", auth);
        Assert.Contains("[EnableRateLimiting(\"MfaPolicy\")]", auth);
    }

    [Fact]
    public void Mfa_setup_refreshes_the_cookie_and_enable_is_atomic()
    {
        var auth = File.ReadAllText(Asset("AuthController.cs"));
        var setup = auth[auth.IndexOf("public async Task<IActionResult> SetupMfa(",
            StringComparison.Ordinal)..auth.IndexOf("// ==================== MFA Enable", StringComparison.Ordinal)];
        var enable = auth[auth.IndexOf("public async Task<IActionResult> EnableMfa(",
            StringComparison.Ordinal)..auth.IndexOf("// ==================== MFA Login", StringComparison.Ordinal)];
        Assert.Contains("ResetAuthenticatorKeyAsync(user)", setup);
        Assert.Contains("AuthenticateAsync(IdentityConstants.ApplicationScheme)", setup);
        Assert.Contains("SignInSessionAsync(user, auth.Properties?.IsPersistent == true, \"pwd\"", setup);
        Assert.Contains("_db.ExecuteInTransactionAsync(async ct =>", enable);
        Assert.Contains("GenerateNewTwoFactorRecoveryCodesAsync", enable);
        Assert.Contains("SetTwoFactorEnabledAsync(user, true)", enable);
        Assert.Contains("UpdateSecurityStampAsync(user)", enable);
        Assert.Contains("await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);", enable);
        Assert.Contains("throw new AuthTransactionRejectedException(", enable);
        Assert.Contains("SignInSessionAsync(user, auth.Properties?.IsPersistent == true, \"mfa\"", enable);
    }

    [Fact]
    public void Password_reset_links_use_a_canonical_domain_instead_of_request_host()
    {
        var auth = File.ReadAllText(Asset("AuthController.cs"));
        var forgot = auth[auth.IndexOf("public async Task<IActionResult> ForgotPassword(",
            StringComparison.Ordinal)..auth.IndexOf("// ==================== Reset Password", StringComparison.Ordinal)];
        Assert.Contains("portalSettings.Value.BaseDomain", auth);
        Assert.Contains("Uri.CheckHostName(domain)", forgot);
        Assert.DoesNotContain("Request.Host", forgot);
        Assert.Contains("\"https://\" + domain.ToLowerInvariant()", forgot);
    }

    [Fact]
    public void Credential_changes_have_a_stricter_per_user_rate_limit()
    {
        var auth = File.ReadAllText(Asset("AuthController.cs"));
        var limiter = File.ReadAllText(Asset("RateLimiterExtensions.cs"));
        Assert.Contains("[EnableRateLimiting(\"AccountSecurityPolicy\")]", auth);
        Assert.Contains("options.AddPolicy(\"AccountSecurityPolicy\"", limiter);
        Assert.Contains("CreateOptions(5, TimeSpan.FromMinutes(15))", limiter);
    }

    [Fact]
    public void Profile_patch_does_not_reset_unrelated_language_preference()
    {
        var dto = new UpdateUserProfileRequestDto { FullName = "Example User" };
        Assert.Null(dto.PreferredLanguage);
        var api = File.ReadAllText(Asset("AuthController.cs"));
        Assert.Contains("var preferred = dto.PreferredLanguage?.Trim()", api);
        Assert.Contains("Unsupported preferred language.", api);
    }
}
