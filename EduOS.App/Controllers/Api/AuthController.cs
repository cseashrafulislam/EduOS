using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces.Jobs;
using Hangfire;
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

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        // ============================================================
        // LOGIN
        // ============================================================
        [EnableRateLimiting("LoginPolicy")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password))
                return BadRequest(new { success = false, message = "Email and password required." });

            var user = await _userManager.FindByEmailAsync(dto.Email);

            // Generic error message for security (don't reveal if email exists)
            if (user == null)
                return BadRequest(new { success = false, message = "Invalid email or password." });

            if (!user.IsActive)
                return BadRequest(new { success = false, message = "Your account has been deactivated. Please contact support." });

            if (!user.EmailConfirmed)
                return BadRequest(new { success = false, message = "Please verify your email before login." });

            // Use lockoutOnFailure: true to enable lockout policy from IdentityExtensions
            var result = await _signInManager.PasswordSignInAsync(
                user,
                dto.Password,
                dto.RememberMe,
                lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                _logger.LogWarning("Account locked: {Email}", dto.Email);
                return BadRequest(new
                {
                    success = false,
                    message = "Account locked due to too many failed attempts. Try again in 15 minutes."
                });
            }

            if (!result.Succeeded)
                return BadRequest(new { success = false, message = "Invalid email or password." });

            // ============================================================
            // CRITICAL: Sign in with custom claims (TenantId + FullName)
            // ============================================================
            await _signInManager.SignOutAsync();

            var claims = new List<Claim>
            {
                new Claim("FullName", user.FullName ?? "")
            };

            // Add TenantId claim (only if user has a tenant; SuperAdmin doesn't)
            if (user.TenantId.HasValue)
            {
                claims.Add(new Claim("TenantId", user.TenantId.Value.ToString()));
            }

            // Add UserType claim
            if (!string.IsNullOrEmpty(user.UserType))
            {
                claims.Add(new Claim("UserType", user.UserType));
            }

            await _signInManager.SignInWithClaimsAsync(user, dto.RememberMe, claims);

            // Update last login
            user.LastLogin = DateTime.UtcNow;
            user.LastLoginIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _userManager.UpdateAsync(user);

            _logger.LogInformation("Login successful: {Email}", dto.Email);

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
                    redirectUrl = await GetRedirectUrlAsync(user)
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
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // Clear tenant cache
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (long.TryParse(userIdStr, out var userId))
            {
                // If you have memory cache for tenant lookup, clear it here
                // _cache.Remove($"tenant:user:{userId}");
            }

            await _signInManager.SignOutAsync();
            return Ok(new { success = true, message = "Logout successful" });
        }

        // ============================================================
        // HELPER: Determine where to redirect after login
        // ============================================================
        private async Task<string> GetRedirectUrlAsync(ApplicationUser user)
        {
            // SuperAdmin → admin dashboard
            if (await _userManager.IsInRoleAsync(user, "SuperAdmin"))
                return "/Admin/Dashboard";

            // No tenant assigned
            if (!user.TenantId.HasValue)
                return "/Account/NoTenant";

            // Tenant user → wizard or dashboard (handled by OnboardingGuard middleware)
            return "/Dashboard";
        }
    }
}
