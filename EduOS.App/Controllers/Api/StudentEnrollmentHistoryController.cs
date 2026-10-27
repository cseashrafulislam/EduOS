using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-enrollments/students/{studentReference:guid}/history")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentEnrollmentHistoryController(
    IStudentEnrollmentHistoryQueryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid studentReference,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var response = await service.GetAsync(studentReference, page, pageSize, ct);
        return StatusCode(response.StatusCode, response);
    }
}
