using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal")]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class AuditLogController : ControllerBase
{
    private readonly IAuditLogService _service;

    public AuditLogController(IAuditLogService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] AuditLogFilterDto filter, CancellationToken cancellationToken)
    {
        var response = await _service.SearchAsync(filter, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics([FromQuery] AuditLogFilterDto filter, CancellationToken cancellationToken)
    {
        var response = await _service.GetStatisticsAsync(filter, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }
}
