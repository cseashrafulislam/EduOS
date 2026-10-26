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
[Route("api/student-guardians/students/{studentReference:guid}/guardians/{guardianReference:guid}/contact")]
public sealed class GuardianContactProfilesController(IGuardianContactProfileService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid studentReference, Guid guardianReference, CancellationToken ct)
    {
        var result = await service.GetAsync(studentReference, guardianReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut]
    public async Task<IActionResult> Update(Guid studentReference, Guid guardianReference,
        [FromBody] UpdateGuardianContactProfileRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.UpdateAsync(studentReference, guardianReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
