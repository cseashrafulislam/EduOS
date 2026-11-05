using EduOS.Core.Common;
using EduOS.Core.DTOs.Platform;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/platform/work-center"), Authorize(Roles = "TenantAdmin,Principal,InventoryManager"),
 EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CentralWorkCenterApiController(ICentralWorkCenterService service) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(int page = 1, int pageSize = 25,
        long? campusId = null, CancellationToken ct = default)
    {
        ApiResponse<CentralWorkCenterPageDto> result = await service.GetPendingAsync(page, pageSize, campusId, ct);
        return StatusCode(result.StatusCode, result);
    }
}
