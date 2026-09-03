using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api
{
    [Authorize(Roles = "TenantAdmin")]
    [AutoValidateAntiforgeryToken]
    [ApiController]
    [Route("api/onboarding")]
    public class OnboardingController : ControllerBase
    {
        private readonly IOnboardingService _onboardingService;

        public OnboardingController(IOnboardingService onboardingService)
        {
            _onboardingService = onboardingService;
        }

        /// <summary>
        /// Returns full onboarding wizard state with all step statuses
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var result = await _onboardingService.GetStatusAsync();
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Mark current step complete and advance to next
        /// </summary>
        [HttpPost("complete-step")]
        public async Task<IActionResult> CompleteStep([FromBody] CompleteStepDto dto)
        {
            var result = await _onboardingService.CompleteStepAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Finalize entire onboarding (validates all required steps)
        /// </summary>
        [HttpPost("complete")]
        public async Task<IActionResult> CompleteOnboarding()
        {
            var result = await _onboardingService.CompleteOnboardingAsync();
            return StatusCode(result.StatusCode, result);
        }
    }
}
