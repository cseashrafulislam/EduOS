using EduOS.App.Authorization;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/hr/recruitment")]
[Authorize(Roles = "TenantAdmin"), RequireModule("HR")]
[AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RecruitmentApiController : ControllerBase
{
    private readonly IRecruitmentService service;
    public RecruitmentApiController(IRecruitmentService service) => this.service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] long? campusId,
        [FromQuery] RecruitmentStage? stage, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var response = await service.ListAsync(search, campusId, stage, page, pageSize, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecruitmentCreateRequestDto request,
        CancellationToken ct)
    {
        var response = await service.CreateAsync(request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("{reference:guid}/transition")]
    public async Task<IActionResult> Transition(Guid reference,
        [FromBody] RecruitmentTransitionRequestDto request, CancellationToken ct)
    {
        var response = await service.TransitionAsync(reference, request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("{reference:guid}/history")]
    public async Task<IActionResult> History(Guid reference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var response = await service.HistoryAsync(reference, page, pageSize, ct);
        return StatusCode(response.StatusCode, response);
    }
}
