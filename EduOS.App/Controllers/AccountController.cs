using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace EduOS.App.Controllers
{
    public class AccountController : Controller
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IMemoryCache _cache;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(
            ICurrentUserService currentUser,
            IMemoryCache cache,
            SignInManager<ApplicationUser> signInManager)
        {
            _currentUser = currentUser;
            _cache = cache;
            _signInManager = signInManager;
        }
        // ==================== Auth Pages ====================

        [HttpGet]
        public IActionResult Login() => View();

        [HttpGet]
        public IActionResult Signup(string? plan = null)
        {
            ViewBag.SelectedPlan = plan;
            return View();
        }

        [HttpGet]
        public IActionResult SignupSuccess() => View();

        [HttpGet]
        public IActionResult VerifyEmail() => View();

        [HttpGet]
        public IActionResult VerifyEmailSuccess() => View();

        [HttpGet]
        public IActionResult VerifyFailed() => View();

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        // ==================== Onboarding Wizard ====================

        [HttpGet]
        public IActionResult InstitutionProfile() => View();

        [HttpGet]
        public IActionResult PlanSelection() => View();

        [HttpGet]
        public IActionResult Payment(long? invoiceId)
        {
            ViewBag.InvoiceId = invoiceId;
            return View();
        }

        [HttpGet]
        public IActionResult PaymentSuccess(string? txn) => View();

        [HttpGet]
        public IActionResult PaymentFailed(string? txn) => View();

        [HttpGet]
        public IActionResult PaymentCancelled(string? txn) => View();

        [HttpGet]
        public IActionResult CampusSetup() => View();

        [HttpGet]
        public IActionResult AcademicSetup() => View();

        [HttpGet]
        public IActionResult BrandingSetup() => View();

        [HttpGet]
        public IActionResult GeneralSettings() => View();

        [HttpGet]
        public IActionResult GatewaySetup() => View();

        [HttpGet]
        public IActionResult OnboardingComplete() => View();

        // ==================== Logout ====================
        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            if (_currentUser.IsAuthenticated)
            {
                var userId = _currentUser.UserId;

                // remove tenant cache
                _cache.Remove($"tenant:user:{userId}");
            }

            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

    }

    // ==================== Public Pricing Page ====================

    public class PricingController : Controller
    {
        [HttpGet]
        public IActionResult Index() => View();
    }
}
