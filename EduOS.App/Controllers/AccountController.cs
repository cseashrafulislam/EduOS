using EduOS.Core.Entities.Auth;
using EduOS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
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

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login() => View();

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Signup(string? plan = null)
        {
            ViewBag.SelectedPlan = plan;
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult SignupSuccess() => View();

        [AllowAnonymous]
        [HttpGet]
        public IActionResult VerifyEmail() => View();

        [AllowAnonymous]
        [HttpGet]
        public IActionResult VerifyEmailSuccess() => View();

        [AllowAnonymous]
        [HttpGet]
        public IActionResult VerifyFailed() => View();

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult MfaChallenge() => View();

        [Authorize(Roles = "SuperAdmin,TenantAdmin,AdmissionOfficer")]
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult MfaSetup() => View();

        // ==================== Onboarding Wizard ====================

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult InstitutionProfile() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult PlanSelection() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult Payment(long? invoiceId)
        {
            ViewBag.InvoiceId = invoiceId;
            return View();
        }

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult PaymentSuccess(string? txn) => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult PaymentFailed(string? txn) => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult PaymentCancelled(string? txn) => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult CampusSetup() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult AcademicSetup() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult ModuleSetup() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult BrandingSetup() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult GeneralSettings() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult GatewaySetup() => View();

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet]
        public IActionResult OnboardingComplete() => View();

        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            return View();
        }

        // ==================== Logout ====================
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            if (_currentUser.IsAuthenticated)
            {
                var userId = _currentUser.UserId;
                _cache.Remove($"tenant:user:{userId}");
            }

            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }
    }

    // ==================== Public Pricing Page ====================

    [AllowAnonymous]
    public class PricingController : Controller
    {
        [HttpGet]
        public IActionResult Index() => View();
    }
}
