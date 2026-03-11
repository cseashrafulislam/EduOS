using EduOS.Core.Entities.Auth;
using EduOS.Core.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Signup()
        {
            return View();
        }


        [HttpGet]
        public IActionResult SignupSuccess()
        {
            return View();
        }
        [HttpGet]
        public IActionResult VerifyEmail()
        {
            return View();
        }


        [HttpGet]
        public IActionResult VerifyEmailSuccess()
        {
            return View();
        }

        [HttpGet]
        public IActionResult VerifyFailed()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }


        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }


        [HttpGet]
        public IActionResult InstitutionProfile()
        {
            return View();
        }

        [HttpGet]
        public IActionResult CampusSetup()
        {
            return View();
        }

        public IActionResult AcademicSetup()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            var userId = UserContext.ResolveUserIdInt();

            if (userId.HasValue)
            {
                // 🔥 tenant cache clear
                UserContext.RemoveTenantCache(userId.Value);
            }

            await _signInManager.SignOutAsync();

            return RedirectToAction("Login", "Account");
        }
    }
}