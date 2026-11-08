using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-counseling")]
[Authorize]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CounselingCasesController(CounselingWorkflowService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> RequestCase(
        [FromBody] OpenCounselingCaseRequest request, CancellationToken ct)
    {
        var result = await service.OpenAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet]
    public async Task<IActionResult> Search(int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.SearchAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await service.MineAsync(page, pageSize, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Status(Guid reference, CancellationToken ct)
    {
        var result = await service.StatusAsync(reference, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/assign")]
    public async Task<IActionResult> Assign(Guid reference,
        [FromBody] AssignCounselorRequest request, CancellationToken ct)
    {
        var result = await service.AssignAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/action")]
    public async Task<IActionResult> Action(Guid reference,
        [FromBody] CounselingCaseActionRequest request, CancellationToken ct)
    {
        var result = await service.ActAsync(reference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
    [HttpPost("{reference:guid}/private-view")]
    public async Task<IActionResult> PrivateView(Guid reference,
        [FromBody] PrivateViewRequest request, CancellationToken ct)
    {
        var result = await service.OpenPrivateAsync(reference, request.RequestId, ct);
        return StatusCode(result.StatusCode, result);
    }
    public sealed class PrivateViewRequest
    {
        public Guid RequestId { get; set; }
    }

    [HttpGet("aggregates")]
    public async Task<IActionResult> Aggregates(int year, int month, CancellationToken ct)
    {
        var result = await service.AggregatesAsync(year, month, ct);
        return StatusCode(result.StatusCode, result);
    }
}
