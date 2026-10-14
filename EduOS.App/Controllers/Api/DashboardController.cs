using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api
{
    [Authorize]
    [EnableRateLimiting("ApiPolicy")]
    [ApiController]
    [Route("api/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        /// <summary>
        /// Returns institution stats, subscription info, onboarding status,
        /// and smart alerts for the dashboard page.
        /// </summary>
        [HttpGet("platform")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> GetPlatform(CancellationToken cancellationToken)
        {
            var result = await _dashboardService.GetPlatformDashboardAsync(cancellationToken);
            Response.Headers.CacheControl = "no-store, private";
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var result = await _dashboardService.GetDashboardAsync();
            return StatusCode(result.StatusCode, result);
        }
    }
}
