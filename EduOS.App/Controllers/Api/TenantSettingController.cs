using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api
{
    [Authorize(Roles = "TenantAdmin,SuperAdmin")]
    [AutoValidateAntiforgeryToken]
    [EnableRateLimiting("ApiPolicy")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ApiController]
    [Route("api/tenant-settings")]
    public class TenantSettingController : ControllerBase
    {
        private readonly ITenantSettingService _settingService;

        public TenantSettingController(ITenantSettingService settingService)
        {
            _settingService = settingService;
        }

        [HttpGet("sms-gateway")]
        public async Task<IActionResult> GetSmsGateway()
        {
            var result = await _settingService.GetSmsGatewayAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("sms-gateway")]
        public async Task<IActionResult> SaveSmsGateway([FromBody] SmsGatewaySettingsDto dto)
        {
            var result = await _settingService.SaveSmsGatewayAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("email-gateway")]
        public async Task<IActionResult> GetEmailGateway()
        {
            var result = await _settingService.GetEmailGatewayAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("email-gateway")]
        public async Task<IActionResult> SaveEmailGateway([FromBody] EmailGatewaySettingsDto dto)
        {
            var result = await _settingService.SaveEmailGatewayAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("category/{category}")]
        public async Task<IActionResult> GetByCategory(string category)
        {
            var result = await _settingService.GetAllByCategoryAsync(category);
            return StatusCode(result.StatusCode, result);
        }
    }
}
