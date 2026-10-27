using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/students/{studentReference:guid}/operational-status")]
public sealed class StudentStatusTransitionsController(IStudentStatusTransitionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid studentReference, CancellationToken ct)
    {
        var result = await service.GetAsync(studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut]
    public async Task<IActionResult> Change(Guid studentReference,
        [FromBody] ChangeStudentOperationalStatusRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.ChangeAsync(studentReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
