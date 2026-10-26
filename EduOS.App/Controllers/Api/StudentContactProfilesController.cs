using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/students/{studentReference:guid}/contact")]
public sealed class StudentContactProfilesController(IStudentContactProfileService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid studentReference, CancellationToken ct)
    {
        var result = await service.GetAsync(studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut]
    public async Task<IActionResult> Update(Guid studentReference,
        [FromBody] UpdateStudentContactProfileRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, message = "Invalid contact profile request." });
        var result = await service.UpdateAsync(studentReference, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
