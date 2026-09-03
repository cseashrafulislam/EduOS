using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api
{
    [Authorize(Roles = "TenantAdmin")]
    [AutoValidateAntiforgeryToken]
    [ApiController]
    [Route("api/institution-onboarding")]
    public class InstitutionOnboardingController : ControllerBase
    {
        private readonly IInstitutionOnboardingService _service;

        public InstitutionOnboardingController(IInstitutionOnboardingService service)
        {
            _service = service;
        }

        // ============================================================
        // PUBLIC (no auth)
        // ============================================================

        /// <summary>
        /// Register a new institution. Creates tenant + admin user + sends email verification.
        /// </summary>
        [AllowAnonymous]
        [EnableRateLimiting("SignupPolicy")]
        [HttpPost("signup")]
        public async Task<IActionResult> Signup([FromBody] InstitutionSignupRequestDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "Invalid request body." });

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return BadRequest(new { success = false, message = string.Join(" | ", errors) });
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await _service.RegisterInstitutionAsync(dto, baseUrl);

            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Email verification link target. Redirects to success or fail page.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail(
            [FromQuery] string email,
            [FromQuery] string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
                return Redirect("/Account/VerifyFailed");

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var ok = await _service.VerifyEmailAsync(email, token, baseUrl);

            return ok
                ? Redirect("/Account/VerifyEmailSuccess")
                : Redirect("/Account/VerifyFailed");
        }

        // ============================================================
        // AUTHENTICATED (onboarding wizard steps)
        // ============================================================

        /// <summary>
        /// Get current institution profile for the logged-in tenant.
        /// </summary>
        [HttpGet("institution-profile")]
        public async Task<IActionResult> GetInstitutionProfile()
        {
            var result = await _service.GetInstitutionProfileAsync();
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Save institution profile details (name, type, owner info, address).
        /// Accepts multipart/form-data (for logo upload in same form if needed).
        /// </summary>
        [HttpPost("institution-profile")]
        [Consumes("multipart/form-data", "application/json")]
        public async Task<IActionResult> SaveInstitutionProfile(
            [FromForm] InstitutionProfileSetupDto dto)
        {
            if (dto == null)
                return BadRequest(new { success = false, message = "Invalid request." });

            var result = await _service.SaveInstitutionProfileAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        // ── Campus ────────────────────────────────────────────────

        /// <summary>
        /// List all campuses for the current tenant.
        /// </summary>
        [HttpGet("campus-list")]
        public async Task<IActionResult> GetCampusList()
        {
            var result = await _service.GetCampusListAsync();
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Get a single campus by ID.
        /// </summary>
        [HttpGet("campus/{id:long}")]
        public async Task<IActionResult> GetCampus(long id)
        {
            var result = await _service.GetCampusByIdAsync(id);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Create or update a campus (Id = null → create, Id set → update).
        /// </summary>
        [HttpPost("campus")]
        public async Task<IActionResult> SaveCampus([FromBody] CampusSetupDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Campus name is required." });

            var result = await _service.SaveCampusAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Soft-delete a campus.
        /// </summary>
        [HttpDelete("campus/{id:long}")]
        public async Task<IActionResult> DeleteCampus(long id)
        {
            var result = await _service.DeleteCampusAsync(id);
            return StatusCode(result.StatusCode, result);
        }

        // ── Academic Year ─────────────────────────────────────────

        [HttpGet("academic-years")]
        public async Task<IActionResult> GetAcademicYears()
        {
            var result = await _service.GetAcademicYearListAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("academic-year/{id:long}")]
        public async Task<IActionResult> GetAcademicYear(long id)
        {
            var result = await _service.GetAcademicYearByIdAsync(id);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("academic-year")]
        public async Task<IActionResult> SaveAcademicYear([FromBody] AcademicYearSetupDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Academic year name is required." });

            var result = await _service.SaveAcademicYearAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("academic-year/{id:long}")]
        public async Task<IActionResult> DeleteAcademicYear(long id)
        {
            var result = await _service.DeleteAcademicYearAsync(id);
            return StatusCode(result.StatusCode, result);
        }

        // ── Academic Term ─────────────────────────────────────────

        [HttpGet("academic-terms")]
        public async Task<IActionResult> GetAcademicTerms()
        {
            var result = await _service.GetAcademicTermListAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("academic-term/{id:long}")]
        public async Task<IActionResult> GetAcademicTerm(long id)
        {
            var result = await _service.GetAcademicTermByIdAsync(id);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("academic-term")]
        public async Task<IActionResult> SaveAcademicTerm([FromBody] AcademicTermSetupDto dto)
        {
            if (dto == null || dto.AcademicYearId <= 0)
                return BadRequest(new { success = false, message = "Academic year is required." });

            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Term name is required." });

            var result = await _service.SaveAcademicTermAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("academic-term/{id:long}")]
        public async Task<IActionResult> DeleteAcademicTerm(long id)
        {
            var result = await _service.DeleteAcademicTermAsync(id);
            return StatusCode(result.StatusCode, result);
        }

        // ── Final ─────────────────────────────────────────────────

        /// <summary>
        /// Mark onboarding complete after campus and academic year are done.
        /// Validates minimum requirements before allowing completion.
        /// </summary>
        [HttpPost("final-complete")]
        public async Task<IActionResult> FinalComplete()
        {
            var result = await _service.FinalCompleteAsync();
            return StatusCode(result.StatusCode, result);
        }
    }
}
