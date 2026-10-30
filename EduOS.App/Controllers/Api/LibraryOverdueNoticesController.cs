using EduOS.App.Authorization;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/library/overdue-notices")]
[Authorize(Roles = "TenantAdmin,Principal,Librarian")]
[RequireModule("LIBRARY")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LibraryOverdueNoticesController(ILibraryOverdueNoticeService service) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] long campusId, CancellationToken ct)
    {
        var result = await service.PreviewAsync(campusId, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] SendLibraryOverdueNoticesRequestDto request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.SendBatchAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
