using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces.Jobs;
using EduOS.Persistence.Context;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace EduOS.App.Controllers.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<AuthController> _logger;
        private readonly EduOSDbContext _db;
        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<AuthController> logger,
            EduOSDbContext db)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _db = db;
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
            var userAgent = HttpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;

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

            // ── 5. Build claims ───────────────────────────────────
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim("FullName", user.FullName ?? string.Empty)
            };

            if (user.TenantId.HasValue)
                claims.Add(new Claim("TenantId", user.TenantId.Value.ToString()));

            if (!string.IsNullOrWhiteSpace(user.UserType))
                claims.Add(new Claim("UserType", user.UserType));

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
                    redirectUrl = await GetRedirectUrlAsync(user.UserType,user.TenantId)
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
            var forwarded = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
                return forwarded.Split(',').FirstOrDefault()?.Trim() ?? string.Empty;

            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        }

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
