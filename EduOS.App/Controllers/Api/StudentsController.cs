using EduOS.App.Authorization;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,AdmissionOfficer")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/students")]
public sealed class StudentsController : ControllerBase
{
    private readonly IStudentDirectoryService _service;
    public StudentsController(IStudentDirectoryService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetPage([FromQuery] StudentDirectoryQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _service.GetPageAsync(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Get(Guid reference, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(reference, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
