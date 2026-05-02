using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api
{
    /// <summary>
    /// Public pricing/plan endpoints. No auth required.
    /// Used on landing page and during onboarding.
    /// </summary>
    [ApiController]
    [Route("api/subscription-plans")]
    [AllowAnonymous]
    public class SubscriptionPlanController : ControllerBase
    {
        private readonly ISubscriptionPlanService _planService;

        public SubscriptionPlanController(ISubscriptionPlanService planService)
        {
            _planService = planService;
        }

        /// <summary>
        /// All publicly visible plans for pricing page
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var result = await _planService.GetPublicPlansAsync();
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Single plan by ID with features
        /// </summary>
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var result = await _planService.GetByIdAsync(id);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Get plan by code (TRIAL, BASIC, PRO, ENTERPRISE)
        /// </summary>
        [HttpGet("by-code/{code}")]
        public async Task<IActionResult> GetByCode(string code)
        {
            var result = await _planService.GetByCodeAsync(code);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Side-by-side feature comparison view
        /// </summary>
        [HttpGet("comparison")]
        public async Task<IActionResult> GetComparison()
        {
            var result = await _planService.GetPlanComparisonAsync();
            return StatusCode(result.StatusCode, result);
        }
    }
}
