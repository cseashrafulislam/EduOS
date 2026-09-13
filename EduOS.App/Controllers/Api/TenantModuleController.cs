using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[Route("api/tenant-modules")]
public class TenantModuleController : ControllerBase
{
    private readonly ITenantModuleService _tenantModuleService;

    public TenantModuleController(ITenantModuleService tenantModuleService)
    {
        _tenantModuleService = tenantModuleService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrentTenantModules()
    {
        var result = await _tenantModuleService.GetCurrentTenantModulesAsync();
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("{moduleCode}/activation")]
    [Authorize(Roles = "TenantAdmin,SuperAdmin")]
    public async Task<IActionResult> UpdateActivation(
        string moduleCode,
        [FromBody] UpdateTenantModuleRequestDto request)
    {
        var result = await _tenantModuleService.UpdateCurrentTenantModuleAsync(
            moduleCode,
            request);
        return StatusCode(result.StatusCode, result);
    }
}
