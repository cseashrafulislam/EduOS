using EduOS.Core.DTOs.Student;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,Registrar")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/students/exit")]
public sealed class StudentExitController : ControllerBase
{
    private readonly IStudentExitService _service;
    public StudentExitController(IStudentExitService service) => _service = service;
    [HttpPost]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Process([FromBody] ProcessStudentExitDto request, CancellationToken cancellationToken) { if (!ModelState.IsValid) return ValidationProblem(ModelState); var result = await _service.ProcessAsync(request, cancellationToken); return StatusCode(result.StatusCode, result); }
    [HttpGet("{studentReference:guid}/history")]
    public async Task<IActionResult> History(Guid studentReference, CancellationToken cancellationToken) { var result = await _service.GetHistoryAsync(studentReference, cancellationToken); return StatusCode(result.StatusCode, result); }
}
