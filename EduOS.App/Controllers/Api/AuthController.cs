using EduOS.App.Security;
using EduOS.App.Localization;
using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using EduOS.Core.Interfaces.Jobs;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.App.Controllers.Api
{
    [ApiController]
    [AutoValidateAntiforgeryToken]
    [Route("api/auth")]
    public partial class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<AuthController> _logger;
        private readonly EduOSDbContext _db;
        private readonly IMfaChallengeService _mfaChallengeService;
        private readonly MfaSettings _mfaSettings;
        private readonly string _portalDomain;

        public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ILogger<AuthController> logger, EduOSDbContext db, IMfaChallengeService mfaChallengeService, IOptions<MfaSettings> mfaSettings, IOptions<TenantPortalSettings> portalSettings)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _db = db;
            _mfaChallengeService = mfaChallengeService;
            _mfaSettings = mfaSettings.Value;
            _portalDomain = portalSettings.Value.BaseDomain;
        }

        // ==================== Login ====================

        [AllowAnonymous]
        [EnableRateLimiting("LoginPolicy")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            if (dto == null || string.IsNullOrWhiteSpace(dto.UserNameOrEmail) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { success = false, message = "Username/email and password are required." });

            var identifier = dto.UserNameOrEmail.Trim();
            var ip = GetClientIp();
            var userAgent = Truncate(HttpContext.Request.Headers["User-Agent"].FirstOrDefault(), 500);
            var user = await _userManager.FindByEmailAsync(identifier) ?? await _userManager.FindByNameAsync(identifier);

            if (user == null)
            {
                await SaveLoginHistoryAsync(null, ip, userAgent, false, "User not found");
                return BadRequest(new { success = false, message = "Invalid username/email or password." });
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("Locked out login attempt for user {UserId}", user.Id);
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Account locked out", dto.TenantId);
                // Do not disclose account existence or lockout state to a caller with a wrong password.
                if (!await _userManager.CheckPasswordAsync(user, dto.Password))
                    return BadRequest(new { success = false, message = "Invalid username/email or password." });
                return BadRequest(new { success = false, message = "Account temporarily locked due to multiple failed attempts. Please try again in 15 minutes." });
            }

            if (!await _userManager.CheckPasswordAsync(user, dto.Password))
            {
                await _userManager.AccessFailedAsync(user);
                _logger.LogWarning("Failed login attempt for user {UserId}", user.Id);
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Wrong password", dto.TenantId);
                return BadRequest(new { success = false, message = "Invalid username/email or password." });
            }

            if (!user.IsActive || user.IsDeleted)
            {
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Account deactivated", dto.TenantId);
                return BadRequest(new { success = false, message = "Your account has been deactivated. Please contact support." });
            }

            if (!user.EmailConfirmed)
            {
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Email not verified", dto.TenantId);
                return BadRequest(new { success = false, message = "Please verify your email before signing in." });
            }

            var session = await GetSessionContextAsync(user, dto.TenantId);
            if (session.RequiresSelection)
                return Conflict(new { success = true, data = new { requiresTenantSelection = true, tenants = session.Tenants } });
            if (!session.IsValid)
            {
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Invalid institution selection or membership", dto.TenantId);
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "An active institution membership is required. Contact your administrator." });
            }

            // No MFA prompt or setup until the user voluntarily enables it in Settings.
            if (user.TwoFactorEnabled)
            {
                var securityStamp = await _userManager.GetSecurityStampAsync(user);
                var challengeToken = _mfaChallengeService.Create(user.Id, securityStamp, dto.RememberMe, session.TenantId);
                Response.Headers.CacheControl = "no-store";

                return StatusCode(StatusCodes.Status202Accepted, new
                {
                    success = true,
                    message = "Multi-factor verification is required.",
                    data = new
                    {
                        requiresTwoFactor = true,
                        challengeToken,
                        redirectUrl = "/Account/MfaChallenge"
                    }
                });
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            await SignInSessionAsync(user, dto.RememberMe, "pwd", session);

            user.LastLoginAt = DateTime.UtcNow;
            user.LastLoginIp = ip;
            user.LastActivityAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                await _signInManager.SignOutAsync();
                _logger.LogError("Failed to update login metadata for user {UserId}: {Errors}", user.Id, string.Join(" | ", updateResult.Errors.Select(x => x.Description)));
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Unable to complete sign in. Please try again." });
            }

            await SaveLoginHistoryAsync(user, ip, userAgent, true, null, session.TenantId);
            _logger.LogInformation("Login successful for user {UserId} from {Ip}", user.Id, ip);

            return Ok(new
            {
                success = true,
                message = "Login successful",
                data = new
                {
                    userId = user.Id,
                    email = user.Email,
                    fullName = user.FullName,
                    requiresTwoFactor = false,
                    userType = session.Role,
                    tenantId = session.TenantId,
                    redirectUrl = GetRedirectUrl(session.Role, session.TenantId)
                }
            });
        }

        // ==================== MFA Status ====================

        [Authorize]
        [HttpGet("mfa/status")]
        public async Task<IActionResult> MfaStatus()
        {
            Response.Headers.CacheControl = "no-store";
            var user = await GetCurrentUserAsync();

            if (user == null)
                return Unauthorized(new { success = false, message = "Invalid session." });

            return Ok(new
            {
                success = true,
                data = new
                {
                    enabled = user.TwoFactorEnabled,
                    sessionVerified = User.HasClaim("amr", "mfa")
                }
            });
        }

        // ==================== MFA Setup ====================

        [Authorize]
        [EnableRateLimiting("MfaPolicy")]
        [HttpPost("mfa/setup")]
        public async Task<IActionResult> SetupMfa([FromBody] MfaSetupRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";

            if (dto == null || string.IsNullOrWhiteSpace(dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Current password is required." });

            var user = await GetCurrentUserAsync();

            if (user == null)
                return Unauthorized(new { success = false, message = "Invalid session." });

            if (user.TwoFactorEnabled)
                return Conflict(new { success = false, message = "Multi-factor authentication is already enabled." });

            if (!await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Current password is incorrect." });

            var session = await GetSessionContextAsync(user);
            if (!session.IsValid)
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Your institution membership is unavailable." });

            var sharedKey = await _userManager.GetAuthenticatorKeyAsync(user);

            if (string.IsNullOrWhiteSpace(sharedKey))
            {
                var reset = await _userManager.ResetAuthenticatorKeyAsync(user);
                if (!reset.Succeeded)
                    return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Authenticator setup failed." });

                sharedKey = await _userManager.GetAuthenticatorKeyAsync(user);
                if (string.IsNullOrWhiteSpace(sharedKey))
                    return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Authenticator setup failed." });

                // Immediately invalidate other cookies after authenticator-secret rotation.
                await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
                // Renew only the current session with its original tenant/role claims.
                var auth = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                await SignInSessionAsync(user, auth.Properties?.IsPersistent == true, "pwd", session);
            }

            if (string.IsNullOrWhiteSpace(sharedKey))
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Authenticator setup failed." });

            var accountLabel = $"EduOS:{user.Email ?? user.UserName ?? user.Id.ToString()}";
            var authenticatorUri = "otpauth://totp/" + Uri.EscapeDataString(accountLabel) + "?secret=" + Uri.EscapeDataString(sharedKey) + "&issuer=" + Uri.EscapeDataString("EduOS") + "&digits=6";

            return Ok(new
            {
                success = true,
                data = new { sharedKey, authenticatorUri }
            });
        }

        // ==================== MFA Enable ====================

        [Authorize]
        [EnableRateLimiting("MfaPolicy")]
        [HttpPost("mfa/enable")]
        public async Task<IActionResult> EnableMfa([FromBody] MfaEnableRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";

            if (dto == null || string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.Code))
                return BadRequest(new { success = false, message = "Password and verification code are required." });

            var user = await GetCurrentUserAsync();

            if (user == null)
                return Unauthorized(new { success = false, message = "Invalid session." });

            if (user.TwoFactorEnabled)
                return Conflict(new { success = false, message = "Multi-factor authentication is already enabled." });

            if (!await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Password or verification code is invalid." });

            var code = NormalizeMfaCode(dto.Code);
            var valid = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);

            if (!valid)
                return BadRequest(new { success = false, message = "Password or verification code is invalid." });

            if (_mfaSettings.RecoveryCodeCount is < 5 or > 20)
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Recovery-code configuration is invalid." });

            var session = await GetSessionContextAsync(user);
            if (!session.IsValid)
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Your institution membership is unavailable." });

            // The canonical unit of work opens transactions inside SQL Server's
            // execution strategy; direct BeginTransactionAsync is incompatible with
            // EnableRetryOnFailure configured in persistence.
            string[] recoveryCodes;
            try
            {
                recoveryCodes = await _db.ExecuteInTransactionAsync(async ct =>
                {
                    var generated = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                        user, _mfaSettings.RecoveryCodeCount))?.ToArray() ?? [];
                    if (generated.Length != _mfaSettings.RecoveryCodeCount)
                        throw new AuthTransactionRejectedException("Recovery codes could not be generated.", 500);

                    var enabled = await _userManager.SetTwoFactorEnabledAsync(user, true);
                    if (!enabled.Succeeded)
                        throw new AuthTransactionRejectedException("MFA setup changed. Reload and try again.");

                    // Invalidate password-only sessions on all other devices.
                    var rotated = await _userManager.UpdateSecurityStampAsync(user);
                    if (!rotated.Succeeded)
                        throw new AuthTransactionRejectedException("Account security changed. Reload and try again.");
                    // Atomic revocation: no old password-only cookie remains valid after MFA activation.
                    await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
                    return generated;
                });
            }
            catch (AuthTransactionRejectedException ex)
            {
                _logger.LogWarning("MFA activation was rolled back for user {UserId}: {Reason}", user.Id, ex.Message);
                return StatusCode(ex.StatusCode, new { success = false, message = ex.Message });
            }

            var auth = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            await SignInSessionAsync(user, auth.Properties?.IsPersistent == true, "mfa", session);
            _logger.LogInformation("MFA enabled for user {UserId}", user.Id);
            return Ok(new
            {
                success = true,
                message = "Multi-factor authentication is enabled.",
                data = new { recoveryCodes }
            });
        }

        // ==================== MFA Login ====================

        [AllowAnonymous]
        [EnableRateLimiting("MfaLoginPolicy")]
        [HttpPost("mfa/login")]
        public async Task<IActionResult> CompleteMfaLogin([FromBody] MfaLoginRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";

            if (dto == null || string.IsNullOrWhiteSpace(dto.ChallengeToken) || string.IsNullOrWhiteSpace(dto.Code) || !_mfaChallengeService.TryRead(dto.ChallengeToken, out var challenge))
                return BadRequest(new { success = false, message = "The verification request is invalid or expired." });

            var user = await _userManager.FindByIdAsync(challenge.UserId.ToString());

            if (user == null || !user.IsActive || user.IsDeleted || !user.EmailConfirmed || !user.TwoFactorEnabled)
                return BadRequest(new { success = false, message = "The verification request is invalid or expired." });

            if (await _userManager.IsLockedOutAsync(user))
                return BadRequest(new { success = false, message = "Account temporarily locked." });

            var currentStamp = await _userManager.GetSecurityStampAsync(user);

            if (!FixedTimeEquals(challenge.SecurityStamp, currentStamp))
                return BadRequest(new { success = false, message = "The verification request is invalid or expired." });

            // Resolve current permissions before consuming a recovery code or login challenge.
            var session = await GetSessionContextAsync(user, challenge.TenantId);
            if (!session.IsValid)
                return StatusCode(StatusCodes.Status403Forbidden, new { success = false, message = "Select an active institution or contact your administrator." });

            var code = dto.UseRecoveryCode ? dto.Code.Trim() : NormalizeMfaCode(dto.Code);
            var valid = dto.UseRecoveryCode
                ? (await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded
                : await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);

            var ip = GetClientIp();
            var userAgent = Truncate(HttpContext.Request.Headers["User-Agent"].FirstOrDefault(), 500);

            if (!valid)
            {
                await _userManager.AccessFailedAsync(user);
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Wrong MFA code", session.TenantId);
                return BadRequest(new { success = false, message = "The verification code is invalid." });
            }

            // Rotate the security stamp with Identity's optimistic concurrency check.
            // Only one request holding the old challenge stamp can complete; the rest fail.
            // This also revokes existing sessions when the cookie validator runs.
            var consumed = await _userManager.UpdateSecurityStampAsync(user);
            if (!consumed.Succeeded)
            {
                _logger.LogWarning("MFA login challenge replay or concurrency conflict for user {UserId}", user.Id);
                return Conflict(new { success = false, message = "Verification was already used or account state changed. Please sign in again." });
            }
            await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
            await _userManager.ResetAccessFailedCountAsync(user);
            await SignInSessionAsync(user, challenge.RememberMe, "mfa", session);

            user.LastLoginAt = DateTime.UtcNow;
            user.LastLoginIp = ip;
            user.LastActivityAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                await _signInManager.SignOutAsync();
                _logger.LogError("Failed to update MFA login metadata for user {UserId}: {Errors}", user.Id, string.Join(" | ", updateResult.Errors.Select(x => x.Description)));
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Unable to complete sign in. Please try again." });
            }

            await SaveLoginHistoryAsync(user, ip, userAgent, true, null, session.TenantId);
            _logger.LogInformation("MFA login successful for user {UserId} from {Ip}", user.Id, ip);

            return Ok(new
            {
                success = true,
                message = "Login successful",
                data = new { redirectUrl = GetRedirectUrl(session.Role, session.TenantId) }
            });
        }

        // ==================== MFA Management (voluntary) ====================

        [Authorize]
        [EnableRateLimiting("AccountSecurityPolicy")]
        [HttpPost("mfa/disable")]
        public async Task<IActionResult> DisableMfa([FromBody] MfaSecurityActionDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
            if (!user.TwoFactorEnabled) return Conflict(new { success = false, message = "MFA is already disabled." });
            if (!User.HasClaim("amr", "mfa"))
                return StatusCode(403, new { success = false, message = "Sign in with MFA before changing authenticator settings." });
            if (dto == null || string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.Code) ||
                !await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Password or verification code is invalid." });
            try
            {
                await _db.ExecuteInTransactionAsync(async ct =>
                {
                    if (!await VerifyMfaActionCodeAsync(user, dto))
                        throw new AuthTransactionRejectedException("Password or verification code is invalid.", 400);
                    var disabled = await _userManager.SetTwoFactorEnabledAsync(user, false);
                    if (!disabled.Succeeded)
                        throw new AuthTransactionRejectedException("MFA state changed. Sign in again.");
                    var reset = await _userManager.ResetAuthenticatorKeyAsync(user);
                    if (!reset.Succeeded)
                        throw new AuthTransactionRejectedException("Authenticator reset failed.");
                    return true;
                });
            }
            catch (AuthTransactionRejectedException ex)
            {
                return StatusCode(ex.StatusCode, new { success = false, message = ex.Message });
            }
            await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
            await _signInManager.SignOutAsync();
            _logger.LogWarning("MFA disabled by user {UserId}", user.Id);
            return Ok(new { success = true, message = "MFA disabled. Sign in again." });
        }

        [Authorize]
        [EnableRateLimiting("AccountSecurityPolicy")]
        [HttpPost("mfa/recovery-codes/regenerate")]
        public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] MfaSecurityActionDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
            if (!user.TwoFactorEnabled || !User.HasClaim("amr", "mfa"))
                return StatusCode(403, new { success = false, message = "Sign in with MFA to manage recovery codes." });
            if (dto == null || string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.Code) ||
                !await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Password or verification code is invalid." });
            if (_mfaSettings.RecoveryCodeCount is < 5 or > 20)
                return StatusCode(500, new { success = false, message = "Recovery-code configuration is invalid." });
            string[] codes;
            try
            {
                codes = await _db.ExecuteInTransactionAsync(async ct =>
                {
                    if (!await VerifyMfaActionCodeAsync(user, dto))
                        throw new AuthTransactionRejectedException("Password or verification code is invalid.", 400);
                    var newCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                        user, _mfaSettings.RecoveryCodeCount))?.ToArray() ?? [];
                    if (newCodes.Length != _mfaSettings.RecoveryCodeCount)
                        throw new AuthTransactionRejectedException("Recovery codes could not be regenerated.");
                    var stamp = await _userManager.UpdateSecurityStampAsync(user);
                    if (!stamp.Succeeded)
                        throw new AuthTransactionRejectedException("Account changed. Retry after signing in.");
                    return newCodes;
                });
            }
            catch (AuthTransactionRejectedException ex)
            {
                return StatusCode(ex.StatusCode, new { success = false, message = ex.Message });
            }
            await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
            await _signInManager.SignOutAsync();
            _logger.LogInformation("MFA recovery codes rotated for user {UserId}", user.Id);
            return Ok(new { success = true, message = "Store these recovery codes securely. Sign in again.",
                data = new { recoveryCodes = codes } });
        }

        private async Task<bool> VerifyMfaActionCodeAsync(ApplicationUser user, MfaSecurityActionDto dto)
        {
            if (dto.UseRecoveryCode)
                return (await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, dto.Code.Trim())).Succeeded;
            return await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, NormalizeMfaCode(dto.Code));
        }

        // ==================== Active Sessions ====================

        [Authorize]
        [HttpGet("sessions")]
        public async Task<IActionResult> GetSessions()
        {
            Response.Headers.CacheControl = "no-store";
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
            var records = await AuthSessionRegistry.ListAsync(_db, user.Id,
                User.FindFirstValue(AuthSessionRegistry.ClaimType));
            return Ok(new { success = true, data = records });
        }

        [Authorize]
        [EnableRateLimiting("AccountSecurityPolicy")]
        [HttpPost("sessions/revoke")]
        public async Task<IActionResult> RevokeSession([FromBody] RevokeUserSessionDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
            if (dto == null || string.IsNullOrWhiteSpace(dto.SessionId))
                return BadRequest(new { success = false, message = "Session reference required." });
            var current = AuthSessionRegistry.IsCurrentReference(
                User.FindFirstValue(AuthSessionRegistry.ClaimType), dto.SessionId);
            if (!current)
            {
                if (user.TwoFactorEnabled && !User.HasClaim("amr", "mfa"))
                    return StatusCode(403, new { success = false, message = "Sign in with MFA to manage other devices." });
                if (string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                    !await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                    return BadRequest(new { success = false, message = "Current password is invalid." });
            }
            var revoked = await AuthSessionRegistry.RevokeByReferenceAsync(_db, user.Id, dto.SessionId);
            if (!revoked) return NotFound(new { success = false, message = "Session no longer active." });
            if (current) await _signInManager.SignOutAsync();
            _logger.LogInformation("User {UserId} revoked {SessionScope} session",
                user.Id, current ? "current" : "other");
            return Ok(new { success = true, message = "Session revoked." });
        }

        [Authorize]
        [EnableRateLimiting("AccountSecurityPolicy")]
        [HttpPost("sessions/revoke-others")]
        public async Task<IActionResult> RevokeOtherSessions([FromBody] MfaSetupRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
            if (user.TwoFactorEnabled && !User.HasClaim("amr", "mfa"))
                return StatusCode(403, new { success = false, message = "Sign in with MFA to manage other devices." });
            if (dto == null || string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                !await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Current password is invalid." });

            var tenantClaim = User.FindFirstValue("TenantId");
            if (tenantClaim != null && (!long.TryParse(tenantClaim, out var parsed) || parsed <= 0))
                return Unauthorized(new { success = false, message = "Session institution is invalid." });
            long? tenantId = tenantClaim == null ? null : long.Parse(tenantClaim);
            var revoked = await AuthSessionRegistry.RevokeOthersAsync(_db, user.Id,
                User.FindFirstValue(AuthSessionRegistry.ClaimType), tenantId);
            if (!revoked.HasValue)
                return Conflict(new { success = false, message = "Session changed. Sign in again." });

            _logger.LogInformation("User {UserId} revoked {Count} other sessions", user.Id, revoked.Value);
            return Ok(new { success = true, message = "Other devices signed out.",
                data = new { revokedCount = revoked.Value } });
        }

        [Authorize]
        [EnableRateLimiting("AccountSecurityPolicy")]
        [HttpPost("sessions/revoke-all")]
        public async Task<IActionResult> RevokeAllSessions([FromBody] MfaSetupRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized(new { success = false, message = "Session unavailable." });
            if (user.TwoFactorEnabled && !User.HasClaim("amr", "mfa"))
                return StatusCode(403, new { success = false, message = "Sign in with MFA to manage sessions." });
            if (dto == null || string.IsNullOrWhiteSpace(dto.CurrentPassword) ||
                !await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Current password is invalid." });
            try
            {
                await _db.ExecuteInTransactionAsync(async ct =>
                {
                    await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
                    var stamp = await _userManager.UpdateSecurityStampAsync(user);
                    if (!stamp.Succeeded)
                        throw new AuthTransactionRejectedException("Concurrent account change. Retry.");
                    return true;
                });
            }
            catch (AuthTransactionRejectedException ex)
            {
                return Conflict(new { success = false, message = ex.Message });
            }
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User {UserId} revoked all sessions", user.Id);
            return Ok(new { success = true, message = "All sessions revoked. Please sign in again." });
        }

        // ==================== Forgot Password ====================

        [AllowAnonymous]
        [EnableRateLimiting("ForgotPasswordPolicy")]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto dto)
        {
            const string message = "If an account exists with that email, a password reset link has been sent.";

            if (dto == null || string.IsNullOrWhiteSpace(dto.Email))
                return Ok(new { success = true, message });

            var email = dto.Email.Trim();
            var user = await _userManager.FindByEmailAsync(email);

            if (user != null && user.IsActive && !user.IsDeleted && user.EmailConfirmed)
            {
                try
                {
                    var domain = _portalDomain?.Trim();
                    if (string.IsNullOrWhiteSpace(domain) || Uri.CheckHostName(domain) != UriHostNameType.Dns)
                    {
                        _logger.LogError("Canonical tenant portal domain is not configured for password reset.");
                        return Ok(new { success = true, message });
                    }
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    // Never construct a password-reset URL from an untrusted Host header.
                    var resetUrl = "https://" + domain.ToLowerInvariant() +
                        "/Account/ResetPassword?email=" + Uri.EscapeDataString(user.Email!) +
                        "&token=" + Uri.EscapeDataString(token);
                    BackgroundJob.Enqueue<IEmailJob>(x => x.SendPasswordResetEmailAsync(user.Email!, user.FullName, resetUrl));
                    _logger.LogInformation("Password reset email queued for user {UserId}", user.Id);
                }
                catch (Exception ex)
                {
                    // A queue/provider outage must not expose whether the supplied
                    // email belongs to an account. Keep the response generic.
                    _logger.LogError(ex, "Password reset email dispatch failed for user {UserId}", user.Id);
                }
            }

            return Ok(new { success = true, message });
        }

        // ==================== Reset Password ====================

        [AllowAnonymous]
        [EnableRateLimiting("ForgotPasswordPolicy")]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword) || string.IsNullOrWhiteSpace(dto.ConfirmPassword))
                return BadRequest(new { success = false, message = "Invalid request." });

            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { success = false, message = "Passwords do not match." });

            var user = await _userManager.FindByEmailAsync(dto.Email.Trim());

            if (user == null || !user.IsActive || user.IsDeleted || !user.EmailConfirmed)
                return BadRequest(new { success = false, message = "Invalid request." });

            var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("Password reset failed for {Email}: {Errors}", dto.Email, errors);
                return BadRequest(new { success = false, message = "Password reset failed. The link may have expired." });
            }

            await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
            _logger.LogInformation("Password reset successfully for user {UserId}", user.Id);
            return Ok(new { success = true, message = "Password reset successful." });
        }

        // ==================== Logout ====================

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // LoginHistory is an append-only access record in the canonical schema.
            // LogoutAt is no longer a mutable field; invalidate the authentication cookie.
            var userId = _userManager.GetUserId(User);
            var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
            if (long.TryParse(userId, out var id) && sid != null)
                await AuthSessionRegistry.RevokeAsync(_db, id, sid);
            await _signInManager.SignOutAsync();
            Response.Headers["Clear-Site-Data"] = "\"cache\"";
            return Ok(new { success = true, message = "Logged out successfully." });
        }

        // ==================== Profile ====================

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            Response.Headers.CacheControl = "no-store";
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized(new { success = false, message = "User session not found." });

            var dto = new
            {
                Id = user.Id,
                FullName = user.FullName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                PreferredLanguage = user.PreferredLanguage,
                PhoneNumber = user.PhoneNumber,
                Address = user.PersonId.HasValue ? await _db.PersonAddresses.AsNoTracking()
                    .Where(x => x.PersonId == user.PersonId.Value && x.IsPrimary && !x.IsDeleted)
                    .Select(x => x.AddressLine1).FirstOrDefaultAsync() : null
            };

            return Ok(new { success = true, data = dto });
        }

        [Authorize]
        [HttpPut("profile")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileRequestDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = GetModelStateError() });

            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized(new { success = false, message = "User session not found." });

            if (dto.Address != null && !string.IsNullOrWhiteSpace(dto.Address) && !user.PersonId.HasValue)
                return Conflict(new { success = false, message = "Link a verified person profile before updating an address." });

            var preferred = dto.PreferredLanguage?.Trim();
            if (!string.IsNullOrEmpty(preferred) && preferred != "bn-BD" && preferred != "en-BD")
                return BadRequest(new { success = false, message = "Unsupported preferred language." });

            string? addressLine;
            try
            {
                addressLine = await _db.ExecuteInTransactionAsync(async ct =>
                {
                    user.FullName = dto.FullName.Trim();
                    if (dto.PhoneNumber != null)
                        user.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();
                    if (dto.PhotoUrl != null)
                        user.PhotoUrl = string.IsNullOrWhiteSpace(dto.PhotoUrl) ? null : dto.PhotoUrl.Trim();
                    if (!string.IsNullOrEmpty(preferred)) user.PreferredLanguage = preferred;
                    user.UpdatedAt = DateTime.UtcNow;
                    PersonAddress? address = null;
                    if (user.PersonId.HasValue)
                    {
                        address = await _db.PersonAddresses.FirstOrDefaultAsync(x =>
                            x.PersonId == user.PersonId.Value && x.IsPrimary && !x.IsDeleted, ct);
                        if (address == null && !string.IsNullOrWhiteSpace(dto.Address))
                        {
                            address = new PersonAddress
                            {
                                PersonId = user.PersonId.Value,
                                IsPrimary = true,
                                AddressTypeCode = "Current",
                                AddressLine1 = dto.Address.Trim()
                            };
                            _db.PersonAddresses.Add(address);
                        }
                        else if (address != null && dto.Address != null)
                            address.AddressLine1 = dto.Address.Trim();
                    }

                    var updated = await _userManager.UpdateAsync(user);
                    if (!updated.Succeeded)
                        throw new AuthTransactionRejectedException(
                            string.Join(" | ", updated.Errors.Select(x => x.Description)), 400);
                    if (user.PersonId.HasValue) await _db.SaveChangesAsync(ct);
                    return address?.AddressLine1;
                });
            }
            catch (AuthTransactionRejectedException ex)
            {
                return StatusCode(ex.StatusCode, new { success = false, message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { success = false, message = "Profile was changed by another request. Reload and retry." });
            }
            return Ok(new
            {
                success = true,
                message = "Profile updated successfully.",
                data = new
                {
                    id = user.Id,
                    fullName = user.FullName,
                    email = user.Email,
                    phoneNumber = user.PhoneNumber,
                    address = addressLine
                }
            });
        }

        // ==================== Change Password ====================

        [Authorize]
        [EnableRateLimiting("AccountSecurityPolicy")]
        [HttpPost("change-password")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, message = GetModelStateError() });

            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { success = false, message = "New password and confirm password do not match." });

            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return Unauthorized(new { success = false, message = "User session not found." });

            if (!await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Current password is incorrect." });

            user.UpdatedAt = DateTime.UtcNow;
            var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

            if (!result.Succeeded)
                return BadRequest(new { success = false, message = string.Join(" | ", result.Errors.Select(x => x.Description)) });

            // Password changes revoke all server-side sessions as well as security stamps.
            await AuthSessionRegistry.RevokeAllAsync(_db, user.Id);
            await _signInManager.SignOutAsync();
            _logger.LogInformation("Password changed successfully for user {UserId}", user.Id);
            return Ok(new { success = true, message = "Password changed. Please sign in again.", redirectUrl = "/Account/Login" });
        }

        private sealed class AuthTransactionRejectedException(string message, int statusCode = 409) : Exception(message)
        {
            public int StatusCode { get; } = statusCode;
        }

        // ==================== Helpers ====================

        private string GetModelStateError() =>
            string.Join(" | ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)));

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.TryParse(value, out var userId) ? await _userManager.FindByIdAsync(userId.ToString()) : null;
        }

        private sealed record TenantChoice(long Id, string Name);
        private sealed record SessionResolution(bool IsValid, bool RequiresSelection,
            string Role, long? TenantId, IReadOnlyList<string> Roles, IReadOnlyList<TenantChoice> Tenants);

        private async Task<SessionResolution> GetSessionContextAsync(ApplicationUser user, long? requestedTenantId = null)
        {
            if (await TenantRoleResolver.IsPlatformAdminAsync(_db, user.Id))
                return new SessionResolution(true, false, "SuperAdmin", null, new[] { "SuperAdmin" }, Array.Empty<TenantChoice>());

            var members = await (from member in _db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
                                 join tenant in _db.Tenants.IgnoreQueryFilters().AsNoTracking()
                                     on member.TenantId equals tenant.Id
                                 where member.UserId == user.Id && !member.IsDeleted &&
                                     member.Status == MembershipStatus.Active && !tenant.IsDeleted &&
                                     (tenant.State == TenantState.Active ||
                                      (tenant.State == TenantState.PendingVerification && tenant.OnboardingStage != OnboardingStage.Completed))
                                 select new { member.TenantId, member.IsOwner, tenant.Name }).ToListAsync();
            var choices = members.GroupBy(x => x.TenantId)
                .Select(x => new TenantChoice(x.Key, x.First().Name)).OrderBy(x => x.Name).ToArray();
            if (choices.Length == 0)
                return new SessionResolution(false, false, "", null, Array.Empty<string>(), choices);

            if (!requestedTenantId.HasValue &&
                User.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString() &&
                long.TryParse(User.FindFirstValue("TenantId"), out var currentTenant))
                requestedTenantId = currentTenant;

            if (!requestedTenantId.HasValue && choices.Length > 1)
                return new SessionResolution(false, true, "", null, Array.Empty<string>(), choices);

            var selected = requestedTenantId ?? choices[0].Id;
            var memberRow = members.FirstOrDefault(x => x.TenantId == selected);
            if (memberRow == null)
                return new SessionResolution(false, false, "", null, Array.Empty<string>(), choices);
            var membershipCount = await _db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.UserId == user.Id && !x.IsDeleted && x.Status != MembershipStatus.Left)
                .Select(x => x.TenantId).Distinct().Take(2).CountAsync();
            var roles = await TenantRoleResolver.GetTenantRolesAsync(_db, user.Id, selected,
                memberRow.IsOwner, membershipCount);
            var role = roles.Contains("TenantAdmin", StringComparer.OrdinalIgnoreCase)
                ? "TenantAdmin" : roles.FirstOrDefault() ?? "User";
            return new SessionResolution(true, false, role, selected, roles, choices);
        }

        private async Task SignInSessionAsync(ApplicationUser user, bool rememberMe,
            string authenticationMethod, SessionResolution session, string? rotatedSid = null)
        {
            var previousSid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
            var sid = rotatedSid ?? await AuthSessionRegistry.CreateAsync(_db, user.Id, session.TenantId,
                GetClientIp(), HttpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "", rememberMe);
            try
            {
                // The default Identity principal can contain global roles that belong to
                // another institution. Replace ALL role claims with server-resolved roles.
                var principal = await _signInManager.CreateUserPrincipalAsync(user);
                var claimsToRemove = new[] { ClaimTypes.Role, "UserType", "TenantId",
                    "amr", "auth_time", "FullName", AuthSessionRegistry.ClaimType };
                foreach (var identity in principal.Identities)
                    foreach (var claim in identity.Claims.Where(c => claimsToRemove.Contains(c.Type)).ToArray())
                        identity.RemoveClaim(claim);
                var primary = principal.Identities.First();
                primary.AddClaims(session.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
                primary.AddClaims(BuildSessionClaims(user, authenticationMethod, session.Role, session.TenantId, sid));
                await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal,
                    new AuthenticationProperties { IsPersistent = rememberMe });
                // Restore the account's saved language on this device after successful sign-in.
                PreferredCultureCookie.Write(HttpContext, user.PreferredLanguage);
            }
            catch
            {
                await AuthSessionRegistry.RevokeAsync(_db, user.Id, sid);
                throw;
            }
            // A tenant switch already exchanged the previous SID transactionally.
            if (rotatedSid == null && previousSid != null)
                await AuthSessionRegistry.RevokeAsync(_db, user.Id, previousSid);
        }

        private static List<Claim> BuildSessionClaims(ApplicationUser user, string authenticationMethod,
            string role, long? tenantId, string sessionId)
        {
            var claims = new List<Claim>
            {
                new("FullName", user.FullName ?? string.Empty),
                new("UserType", role),
                new("amr", authenticationMethod),
                new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new(AuthSessionRegistry.ClaimType, sessionId)
            };
            if (tenantId.HasValue) claims.Add(new Claim("TenantId", tenantId.Value.ToString()));
            return claims;
        }

        private static string NormalizeMfaCode(string code) =>
            code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

        private static bool FixedTimeEquals(string first, string second)
        {
            var firstBytes = Encoding.UTF8.GetBytes(first);
            var secondBytes = Encoding.UTF8.GetBytes(second);

            try
            {
                return firstBytes.Length == secondBytes.Length && CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(firstBytes);
                CryptographicOperations.ZeroMemory(secondBytes);
            }
        }

        private async Task SaveLoginHistoryAsync(ApplicationUser? user, string ip, string userAgent, bool isSuccess,
            string? failReason, long? requestedTenantId = null)
        {
            try
            {
                long? tenantId = null;
                if (user != null && requestedTenantId.HasValue)
                {
                    // A login request may contain an arbitrary TenantId. Only attribute
                    // the event to that institute after verifying this account's active
                    // membership and the institute's current login eligibility.
                    var allowed = await (from member in _db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
                                         join tenant in _db.Tenants.IgnoreQueryFilters().AsNoTracking()
                                             on member.TenantId equals tenant.Id
                                         where member.UserId == user.Id && member.TenantId == requestedTenantId.Value &&
                                               !member.IsDeleted && member.Status == MembershipStatus.Active &&
                                               !tenant.IsDeleted &&
                                               (tenant.State == TenantState.Active ||
                                                (tenant.State == TenantState.PendingVerification &&
                                                 tenant.OnboardingStage != OnboardingStage.Completed))
                                         select member.TenantId).AnyAsync();
                    if (allowed) tenantId = requestedTenantId.Value;
                }
                else if (user != null && !isSuccess)
                {
                    // Before institution selection, a failed password attempt can be
                    // attributed only if the account has exactly one active membership.
                    // Multi-institute/global attempts remain unassigned, never guessed.
                    var allowed = await (from member in _db.TenantMemberships.IgnoreQueryFilters().AsNoTracking()
                                         join tenant in _db.Tenants.IgnoreQueryFilters().AsNoTracking()
                                             on member.TenantId equals tenant.Id
                                         where member.UserId == user.Id && !member.IsDeleted &&
                                               member.Status == MembershipStatus.Active && !tenant.IsDeleted &&
                                               (tenant.State == TenantState.Active ||
                                                (tenant.State == TenantState.PendingVerification &&
                                                 tenant.OnboardingStage != OnboardingStage.Completed))
                                         select member.TenantId).Distinct().Take(2).ToArrayAsync();
                    if (allowed.Length == 1) tenantId = allowed[0];
                }
                _db.LoginHistories.Add(new LoginHistory
                {
                    UserId = user?.Id,
                    TenantId = tenantId,
                    OccurredAt = DateTime.UtcNow,
                    IpAddress = ip,
                    UserAgent = userAgent,
                    IsSuccess = isSuccess,
                    FailureReason = failReason
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to append login history for user {UserId}", user?.Id);
            }
        }

        private static string GetRedirectUrl(string userType, long? tenantId)
        {
            if (string.Equals(userType, "SuperAdmin", StringComparison.Ordinal))
                return "/Dashboard/Admin";

            if (!tenantId.HasValue)
                return "/Account/Login?error=no_tenant";

            return "/Dashboard/Index";
        }

        private string GetClientIp() =>
            Truncate(HttpContext.Connection.RemoteIpAddress?.ToString(), 64);

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            return trimmed[..Math.Min(trimmed.Length, maxLength)];
        }

        private static (string browser, string device) ParseUserAgent(string ua)
        {
            if (string.IsNullOrWhiteSpace(ua))
                return ("Unknown", "Unknown");

            var browser = ua switch
            {
                _ when ua.Contains("Edg/") => "Edge",
                _ when ua.Contains("Chrome") => "Chrome",
                _ when ua.Contains("Firefox") => "Firefox",
                _ when ua.Contains("Safari") && !ua.Contains("Chrome") => "Safari",
                _ when ua.Contains("Opera") || ua.Contains("OPR") => "Opera",
                _ => "Other"
            };

            var device = ua switch
            {
                _ when ua.Contains("Mobile") => "Mobile",
                _ when ua.Contains("iPad") || ua.Contains("Tablet") => "Tablet",
                _ when ua.Contains("Android") => "Android",
                _ => "Desktop"
            };

            return (browser, device);
        }
    }
}
