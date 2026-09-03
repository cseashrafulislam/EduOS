using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces.Jobs;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

namespace EduOS.App.Controllers.Api
{
    [ApiController]
    [AutoValidateAntiforgeryToken]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<AuthController> _logger;
        private readonly EduOSDbContext _db;
        private readonly IMfaChallengeService _mfaChallengeService;
        private readonly MfaSettings _mfaSettings;
        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<AuthController> logger,
            EduOSDbContext db,
            IMfaChallengeService mfaChallengeService,
            IOptions<MfaSettings> mfaSettings)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _db = db;
            _mfaChallengeService = mfaChallengeService;
            _mfaSettings = mfaSettings.Value;
        }

        // ============================================================
        // LOGIN
        // ============================================================
        [EnableRateLimiting("LoginPolicy")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
        {
            // ── 1. Basic Validation ───────────────────────────────
            if (dto == null
                || string.IsNullOrWhiteSpace(dto.Email)
                || string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new { success = false, message = "Email and password are required." });
            }

            var ip = GetClientIp();
            var userAgent = Truncate(HttpContext.Request.Headers["User-Agent"].FirstOrDefault(), 500);

            // ── 2. Find user ──────────────────────────────────────
            var user = await _userManager.FindByEmailAsync(dto.Email.Trim().ToLower());

            if (user == null)
            {
                await SaveLoginHistoryAsync(null, dto.Email, ip, userAgent, false, "User not found");
                return BadRequest(new { success = false, message = "Invalid email or password." });
            }

            // ── 3. Security checks ────────────────────────────────
            if (!user.IsActive)
            {
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Account deactivated");
                return BadRequest(new
                {
                    success = false,
                    message = "Your account has been deactivated. Please contact support."
                });
            }

            if (!user.EmailConfirmed)
            {
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Email not verified");
                return BadRequest(new
                {
                    success = false,
                    message = "Please verify your email before signing in."
                });
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("Locked out login attempt: {Email}", dto.Email);
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Account locked out");
                return BadRequest(new
                {
                    success = false,
                    message = "Account temporarily locked due to multiple failed attempts. Please try again in 15 minutes."
                });
            }

            // ── 4. Password check ─────────────────────────────────
            var isPasswordValid = await _userManager.CheckPasswordAsync(user, dto.Password);

            if (!isPasswordValid)
            {
                await _userManager.AccessFailedAsync(user);
                _logger.LogWarning("Failed login attempt: {Email}", dto.Email);
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Wrong password");
                return BadRequest(new { success = false, message = "Invalid email or password." });
            }

            // Reset failed count on successful auth
            await _userManager.ResetAccessFailedCountAsync(user);

            var roles = await _userManager.GetRolesAsync(user);
            var isPrivileged = IsPrivileged(roles);

            if (user.TwoFactorEnabled)
            {
                var securityStamp = await _userManager.GetSecurityStampAsync(user);
                var challengeToken = _mfaChallengeService.Create(
                    user.Id,
                    securityStamp,
                    dto.RememberMe);

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

            // ── 5. Build claims ───────────────────────────────────
            var claims = BuildSessionClaims(user, "pwd");

            // ── 6. Sign in with claims ────────────────────────────
            await _signInManager.SignInWithClaimsAsync(user, dto.RememberMe, claims);

            // ── 7. Update user metadata ───────────────────────────
            user.LastLogin = DateTime.UtcNow;
            user.LastLoginIp = ip;
            user.LastActivityAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            // ── 8. Log success ────────────────────────────────────
            await SaveLoginHistoryAsync(user, ip, userAgent, true, null);
            _logger.LogInformation("Login successful: {Email} from {Ip}", dto.Email, ip);

            // ── 9. Response ───────────────────────────────────────
            return Ok(new
            {
                success = true,
                message = "Login successful",
                data = new
                {
                    userId = user.Id,
                    email = user.Email,
                    fullName = user.FullName,
                    userType = user.UserType,
                    tenantId = user.TenantId,
                    redirectUrl = isPrivileged
                        ? "/Account/MfaSetup"
                        : await GetRedirectUrlAsync(user.UserType,user.TenantId)
                }
            });
        }

        // ============================================================
        // MULTI-FACTOR AUTHENTICATION
        // ============================================================
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

            var sharedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(sharedKey))
            {
                var reset = await _userManager.ResetAuthenticatorKeyAsync(user);
                if (!reset.Succeeded)
                    return StatusCode(500, new { success = false, message = "Authenticator setup failed." });
                sharedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            if (string.IsNullOrWhiteSpace(sharedKey))
                return StatusCode(500, new { success = false, message = "Authenticator setup failed." });

            var accountLabel = $"EduOS:{user.Email ?? user.UserName ?? user.Id.ToString()}";
            var authenticatorUri = "otpauth://totp/"
                + Uri.EscapeDataString(accountLabel)
                + "?secret=" + Uri.EscapeDataString(sharedKey)
                + "&issuer=" + Uri.EscapeDataString("EduOS")
                + "&digits=6";

            return Ok(new
            {
                success = true,
                data = new
                {
                    sharedKey,
                    authenticatorUri
                }
            });
        }

        [Authorize]
        [EnableRateLimiting("MfaPolicy")]
        [HttpPost("mfa/enable")]
        public async Task<IActionResult> EnableMfa([FromBody] MfaEnableRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            if (dto == null
                || string.IsNullOrWhiteSpace(dto.CurrentPassword)
                || string.IsNullOrWhiteSpace(dto.Code))
            {
                return BadRequest(new { success = false, message = "Password and verification code are required." });
            }

            var user = await GetCurrentUserAsync();
            if (user == null)
                return Unauthorized(new { success = false, message = "Invalid session." });

            if (user.TwoFactorEnabled)
                return Conflict(new { success = false, message = "Multi-factor authentication is already enabled." });

            if (!await _userManager.CheckPasswordAsync(user, dto.CurrentPassword))
                return BadRequest(new { success = false, message = "Password or verification code is invalid." });

            var code = NormalizeMfaCode(dto.Code);
            var valid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                code);
            if (!valid)
                return BadRequest(new { success = false, message = "Password or verification code is invalid." });

            if (_mfaSettings.RecoveryCodeCount is < 5 or > 20)
                return StatusCode(500, new { success = false, message = "Recovery-code configuration is invalid." });

            var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                    user,
                    _mfaSettings.RecoveryCodeCount))
                ?.ToArray() ?? [];
            if (recoveryCodes.Length != _mfaSettings.RecoveryCodeCount)
                return StatusCode(500, new { success = false, message = "Recovery codes could not be generated." });

            var enabled = await _userManager.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded)
                return StatusCode(500, new { success = false, message = "Multi-factor authentication could not be enabled." });

            await _signInManager.SignInWithClaimsAsync(
                user,
                isPersistent: false,
                BuildSessionClaims(user, "mfa"));

            return Ok(new
            {
                success = true,
                message = "Multi-factor authentication is enabled.",
                data = new { recoveryCodes }
            });
        }

        [AllowAnonymous]
        [EnableRateLimiting("MfaPolicy")]
        [HttpPost("mfa/login")]
        public async Task<IActionResult> CompleteMfaLogin([FromBody] MfaLoginRequestDto dto)
        {
            Response.Headers.CacheControl = "no-store";
            if (dto == null
                || string.IsNullOrWhiteSpace(dto.ChallengeToken)
                || string.IsNullOrWhiteSpace(dto.Code)
                || !_mfaChallengeService.TryRead(dto.ChallengeToken, out var challenge))
            {
                return BadRequest(new { success = false, message = "The verification request is invalid or expired." });
            }

            var user = await _userManager.FindByIdAsync(challenge.UserId.ToString());
            if (user == null || !user.IsActive || !user.EmailConfirmed || !user.TwoFactorEnabled)
                return BadRequest(new { success = false, message = "The verification request is invalid or expired." });

            if (await _userManager.IsLockedOutAsync(user))
                return BadRequest(new { success = false, message = "Account temporarily locked." });

            var currentStamp = await _userManager.GetSecurityStampAsync(user);
            if (!FixedTimeEquals(challenge.SecurityStamp, currentStamp))
                return BadRequest(new { success = false, message = "The verification request is invalid or expired." });

            var code = dto.UseRecoveryCode
                ? dto.Code.Trim()
                : NormalizeMfaCode(dto.Code);
            var valid = dto.UseRecoveryCode
                ? (await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded
                : await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    TokenOptions.DefaultAuthenticatorProvider,
                    code);

            var ip = GetClientIp();
            var userAgent = Truncate(HttpContext.Request.Headers["User-Agent"].FirstOrDefault(), 500);
            if (!valid)
            {
                await _userManager.AccessFailedAsync(user);
                await SaveLoginHistoryAsync(user, ip, userAgent, false, "Wrong MFA code");
                return BadRequest(new { success = false, message = "The verification code is invalid." });
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            await _signInManager.SignInWithClaimsAsync(
                user,
                challenge.RememberMe,
                BuildSessionClaims(user, "mfa"));

            user.LastLogin = DateTime.UtcNow;
            user.LastLoginIp = ip;
            user.LastActivityAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            await SaveLoginHistoryAsync(user, ip, userAgent, true, null);

            return Ok(new
            {
                success = true,
                message = "Login successful",
                data = new
                {
                    redirectUrl = await GetRedirectUrlAsync(user.UserType, user.TenantId)
                }
            });
        }

        // ============================================================
        // FORGOT PASSWORD
        // ============================================================
        [EnableRateLimiting("ForgotPasswordPolicy")]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDto dto)
        {
            // Always return success - don't reveal if email exists
            var user = await _userManager.FindByEmailAsync(dto.Email);

            if (user != null && user.IsActive && user.EmailConfirmed)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                var resetUrl = $"{baseUrl}/Account/ResetPassword" +
                              $"?email={Uri.EscapeDataString(user.Email!)}" +
                              $"&token={Uri.EscapeDataString(token)}";

                BackgroundJob.Enqueue<IEmailJob>(x =>
                    x.SendPasswordResetEmailAsync(user.Email!, user.FullName, resetUrl));

                _logger.LogInformation("Password reset email sent: {Email}", dto.Email);
            }

            return Ok(new
            {
                success = true,
                message = "If an account exists with that email, a password reset link has been sent."
            });
        }

        // ============================================================
        // RESET PASSWORD
        // ============================================================
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequestDto dto)
        {
            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { success = false, message = "Passwords do not match." });

            var user = await _userManager.FindByEmailAsync(dto.Email);

            // Generic message for security
            if (user == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            var result = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("Password reset failed for {Email}: {Errors}", dto.Email, errors);
                return BadRequest(new { success = false, message = "Password reset failed. The link may have expired." });
            }

            _logger.LogInformation("Password reset: {Email}", dto.Email);

            return Ok(new { success = true, message = "Password reset successful." });
        }

        
        // ============================================================
        // LOGOUT
        // ============================================================
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                // Update latest login history with logout time
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (long.TryParse(userIdStr, out var userId))
                {
                    var history = _db.LoginHistories
                        .Where(h => h.UserId == userId && h.LogoutAt == null && h.IsSuccess)
                        .OrderByDescending(h => h.LoginAt)
                        .FirstOrDefault();

                    if (history != null)
                    {
                        history.LogoutAt = DateTime.UtcNow;
                        _db.LoginHistories.Update(history);
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating logout time");
            }

            await _signInManager.SignOutAsync();

            return Ok(new { success = true, message = "Logged out successfully." });
        }


        // ============================================================
        // PRIVATE HELPERS
        // ============================================================
        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.TryParse(value, out var userId)
                ? await _userManager.FindByIdAsync(userId.ToString())
                : null;
        }

        private static List<Claim> BuildSessionClaims(ApplicationUser user, string authenticationMethod)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email ?? string.Empty),
                new("FullName", user.FullName ?? string.Empty),
                new("amr", authenticationMethod),
                new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            };

            if (user.TenantId.HasValue)
                claims.Add(new Claim("TenantId", user.TenantId.Value.ToString()));

            if (!string.IsNullOrWhiteSpace(user.UserType))
                claims.Add(new Claim("UserType", user.UserType));

            return claims;
        }

        private static bool IsPrivileged(IEnumerable<string> roles) =>
            roles.Any(role => role is "SuperAdmin" or "TenantAdmin");

        private static string NormalizeMfaCode(string code) =>
            code.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

        private static bool FixedTimeEquals(string first, string second)
        {
            var firstBytes = Encoding.UTF8.GetBytes(first);
            var secondBytes = Encoding.UTF8.GetBytes(second);
            try
            {
                return firstBytes.Length == secondBytes.Length
                    && CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(firstBytes);
                CryptographicOperations.ZeroMemory(secondBytes);
            }
        }

        private async Task SaveLoginHistoryAsync(
            ApplicationUser user,
            string ip,
            string userAgent,
            bool isSuccess,
            string? failReason)
        {
            await SaveLoginHistoryAsync(user, null, ip, userAgent, isSuccess, failReason);
        }

        private async Task SaveLoginHistoryAsync(
            ApplicationUser? user,
            string? attemptedEmail,
            string ip,
            string userAgent,
            bool isSuccess,
            string? failReason)
        {
            try
            {
                // Parse browser and device from User-Agent
                var (browser, device) = ParseUserAgent(userAgent);

                var history = new LoginHistory
                {
                    UserId = user?.Id ?? 0,
                    TenantId = user?.TenantId ?? 0,
                    LoginAt = DateTime.UtcNow,
                    IpAddress = ip,
                    UserAgent = userAgent,
                    Browser = browser,
                    Device = device,
                    IsSuccess = isSuccess,
                    FailReason = failReason
                };

                _db.LoginHistories.Add(history);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Never let login history failure break the login flow
                _logger.LogError(ex, "Failed to save login history for {Email}",
                    user?.Email ?? attemptedEmail ?? "unknown");
            }
        }

        private async Task<string> GetRedirectUrlAsync(string userType, long? tenantId)
        {
            // SuperAdmin → admin dashboard
            if (userType == "SuperAdmin")
                return "/Dashboard/Admin";

            // No tenant assigned
            if (!tenantId.HasValue)
                return "/Account/Login?error=no_tenant";

            // Tenant user → OnboardingGuard middleware handles redirect to wizard if incomplete
            return "/Dashboard/Index";
        }

        private string GetClientIp()
        {
            // RemoteIpAddress is authoritative after ASP.NET Forwarded Headers is
            // configured with trusted proxies. Never trust a raw client-supplied
            // X-Forwarded-For value here.
            return Truncate(HttpContext.Connection.RemoteIpAddress?.ToString(), 64);
        }

        private static string Truncate(string? value, int maxLength) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

        private static (string browser, string device) ParseUserAgent(string ua)
        {
            if (string.IsNullOrEmpty(ua))
                return ("Unknown", "Unknown");

            string browser = ua switch
            {
                _ when ua.Contains("Edg/") => "Edge",
                _ when ua.Contains("Chrome") => "Chrome",
                _ when ua.Contains("Firefox") => "Firefox",
                _ when ua.Contains("Safari") && !ua.Contains("Chrome") => "Safari",
                _ when ua.Contains("Opera") || ua.Contains("OPR") => "Opera",
                _ => "Other"
            };

            string device = ua switch
            {
                _ when ua.Contains("Mobile") || ua.Contains("Android") && ua.Contains("Mobile") => "Mobile",
                _ when ua.Contains("iPad") || ua.Contains("Tablet") => "Tablet",
                _ when ua.Contains("Android") => "Android",
                _ => "Desktop"
            };

            return (browser, device);
        }

    }
}
