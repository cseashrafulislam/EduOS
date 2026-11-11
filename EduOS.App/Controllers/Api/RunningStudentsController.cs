using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/students/running")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RunningStudentsController(StudentQuickEntryService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRunningStudentRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await service.CreateAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
