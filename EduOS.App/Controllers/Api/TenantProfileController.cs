using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api
{
    [Authorize(Roles = "TenantAdmin,SuperAdmin")]
    [AutoValidateAntiforgeryToken]
    [ApiController]
    [Route("api/tenant-profile")]
    public class TenantProfileController : ControllerBase
    {
        private readonly ITenantProfileService _profileService;

        public TenantProfileController(ITenantProfileService profileService)
        {
            _profileService = profileService;
        }

        // ============================================================
        // PROFILE
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var result = await _profileService.GetProfileAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        public async Task<IActionResult> Update([FromBody] UpdateTenantProfileDto dto)
        {
            var result = await _profileService.UpdateProfileAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // BRANDING
        // ============================================================
        [HttpPut("branding")]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        public async Task<IActionResult> UpdateBranding([FromBody] UpdateBrandingDto dto)
        {
            var result = await _profileService.UpdateBrandingAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("logo")]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadLogo(IFormFile file)
        {
            var result = await _profileService.UploadLogoAsync(file);
            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("logo")]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        public async Task<IActionResult> RemoveLogo()
        {
            var result = await _profileService.RemoveLogoAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("favicon")]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadFavicon(IFormFile file)
        {
            var result = await _profileService.UploadFaviconAsync(file);
            return StatusCode(result.StatusCode, result);
        }

        [HttpDelete("favicon")]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        public async Task<IActionResult> RemoveFavicon()
        {
            var result = await _profileService.RemoveFaviconAsync();
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // SUBDOMAIN
        // ============================================================
        [HttpGet("subdomain/check")]
        public async Task<IActionResult> CheckSubdomain([FromQuery] string subdomain)
        {
            var result = await _profileService.CheckSubdomainAvailabilityAsync(subdomain);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPut("subdomain")]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        public async Task<IActionResult> UpdateSubdomain([FromBody] UpdateSubdomainDto dto)
        {
            var result = await _profileService.UpdateSubdomainAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // GENERAL SETTINGS
        // ============================================================
        [HttpPut("general-settings")]
        [Authorize(Roles = "TenantAdmin,SuperAdmin")]
        public async Task<IActionResult> UpdateGeneralSettings([FromBody] UpdateGeneralSettingsDto dto)
        {
            var result = await _profileService.UpdateGeneralSettingsAsync(dto);
            return StatusCode(result.StatusCode, result);
        }
    }
}
