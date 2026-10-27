using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/students/{studentReference:guid}/lifecycle-timeline")]
public sealed class StudentLifecycleTimelineController : ControllerBase
{
    private readonly IStudentLifecycleTimelineService _service;
    public StudentLifecycleTimelineController(IStudentLifecycleTimelineService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(Guid studentReference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetPageAsync(studentReference, page, pageSize, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
