using EduOS.Core.DTOs.Auth;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Helpers;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IUnitOfWork _unitOfWork;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IUnitOfWork unitOfWork)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _unitOfWork = unitOfWork;
        }


        [EnableRateLimiting("LoginPolicy")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            var user = await _userManager.FindByEmailAsync(dto.Email);

            if (user == null)
                return BadRequest(new { success = false, message = "Invalid email or password." });

            if (!user.EmailConfirmed)
                return BadRequest(new { success = false, message = "Please verify your email before login." });

            var result = await _signInManager.PasswordSignInAsync(
                user,
                dto.Password,
                dto.RememberMe,
                lockoutOnFailure: false);

            if (!result.Succeeded)
                return BadRequest(new { success = false, message = "Invalid email or password." });
            
            var isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");

           // var tenantUser = await _unitOfWork.TenantUsers
               // .FirstOrDefaultAsync(x => x.UserId == user.Id && x.IsActive);

            //if (!isSuperAdmin && tenantUser == null)
            //{
            //    await _signInManager.SignOutAsync();

            //    return BadRequest(new
            //    {
            //        success = false,
            //        message = "No active tenant assigned to this user.",
            //        userId = user.Id,
            //        tenantId = tenantUser.TenantId
            //    });
            //}

            // UserContext.SetTenantCache(user.Id, (int)tenantUser.TenantId);

            return Ok(new
            {
                success = true,
                message = "Login success"
            });
        }



        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);

            if (user == null)
                return Ok(new { success = true });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var resetUrl =
                $"{baseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";

            BackgroundJob.Enqueue<IEmailJob>(x =>
                x.SendPasswordResetEmailAsync(
                    user.Email!,
                    user.FullName,
                    resetUrl
                ));

            return Ok(new
            {
                success = true,
                message = "Password reset link sent to your email."
            });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequestDto dto)
        {
            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { message = "Passwords do not match." });

            var user = await _userManager.FindByEmailAsync(dto.Email);

            if (user == null)
                return BadRequest(new { message = "Invalid user." });

            var result = await _userManager.ResetPasswordAsync(
                user,
                dto.Token,
                dto.NewPassword);

            if (!result.Succeeded)
                return BadRequest(new { message = "Password reset failed." });

            return Ok(new
            {
                success = true,
                message = "Password reset successful."
            });
        } 



        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = UserContext.ResolveUserIdInt();

            if (userId.HasValue)
                UserContext.RemoveTenantCache(userId.Value);

            await _signInManager.SignOutAsync();

            return Ok(new
            {
                success = true,
                message = "Logout success"
            });
        }
    }
}