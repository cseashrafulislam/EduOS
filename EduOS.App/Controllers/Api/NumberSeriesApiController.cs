using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/number-series")]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NumberSeriesApiController(INumberSeriesService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int page = 1, int pageSize = 20,
        string? search = null, CancellationToken ct = default)
    { var r = await service.GetSeriesAsync(page, pageSize, search, ct); return StatusCode(r.StatusCode, r); }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveNumberSeriesRequestDto request, CancellationToken ct)
    { var r = await service.SaveSeriesAsync(null, request, ct); return StatusCode(r.StatusCode, r); }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveNumberSeriesRequestDto request, CancellationToken ct)
    { var r = await service.SaveSeriesAsync(id, request, ct); return StatusCode(r.StatusCode, r); }
}
