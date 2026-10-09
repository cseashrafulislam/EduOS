using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,SuperAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/tenant-profile")]
public sealed class TenantProfileController : ControllerBase
{
    private readonly ITenantProfileService _service;
    public TenantProfileController(ITenantProfileService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _service.GetProfileAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Update([FromBody] UpdateTenantProfileRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateProfileAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("regional-settings")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateRegionalSettings([FromBody] UpdateTenantRegionalSettingsRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateRegionalSettingsAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("logo")]
    [Authorize(Roles = "TenantAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadLogo(IFormFile file, CancellationToken cancellationToken) =>
        await UploadAsync(file, false, cancellationToken);

    [HttpPost("favicon")]
    [Authorize(Roles = "TenantAdmin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadFavicon(IFormFile file, CancellationToken cancellationToken) =>
        await UploadAsync(file, true, cancellationToken);

    [HttpDelete("logo")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> RemoveLogo([FromQuery] string rowVersion, CancellationToken cancellationToken)
    {
        var result = await _service.RemoveLogoAsync(rowVersion, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpDelete("favicon")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> RemoveFavicon([FromQuery] string rowVersion, CancellationToken cancellationToken)
    {
        var result = await _service.RemoveFaviconAsync(rowVersion, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("subdomain/check")]
    public async Task<IActionResult> CheckSubdomain([FromQuery] string subdomain, CancellationToken cancellationToken)
    {
        var result = await _service.CheckSubdomainAvailabilityAsync(subdomain, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("subdomain")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateSubdomain([FromBody] UpdateTenantSubdomainRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateSubdomainAsync(request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    private async Task<IActionResult> UploadAsync(IFormFile file, bool favicon, CancellationToken ct)
    {
        if (file == null || file.Length <= 0 || file.Length > 5 * 1024L * 1024L)
            return BadRequest(new { success = false, message = "Valid image (max 5MB) required." });
        await using var stream = file.OpenReadStream();
        var dto = new PrivateFileUploadDto
        {
            FileName = file.FileName, ContentType = file.ContentType,
            Length = file.Length, Content = stream
        };
        var result = favicon
            ? await _service.UploadFaviconAsync(dto, ct)
            : await _service.UploadLogoAsync(dto, ct);
        return StatusCode(result.StatusCode, result);
    }
}
