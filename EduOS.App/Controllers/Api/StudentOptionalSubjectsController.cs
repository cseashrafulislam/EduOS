using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Academic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-optional-subjects")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Student,Guardian,Parent")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentOptionalSubjectsController : ControllerBase
{
    private readonly IStudentOptionalSubjectQueryService _query;

    public StudentOptionalSubjectsController(IStudentOptionalSubjectQueryService implementation) => _query = implementation;

    [HttpGet("students/{studentReference:guid}")]
    public async Task<IActionResult> Get(Guid studentReference, CancellationToken ct)
    {
        var result = await _query.GetAsync(studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }
}
