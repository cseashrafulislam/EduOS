using EduOS.App.Authorization;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/student-guardians")]
[Authorize(Roles = "TenantAdmin")]
[RequireModule("STUDENT")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentGuardiansController : ControllerBase
{
    private readonly IStudentGuardianWorkspaceService _service;

    // Platform owns the global DI module; scoped construction until interface registration.
    public StudentGuardiansController(IServiceProvider services) =>
        _service = ActivatorUtilities.CreateInstance<StudentGuardianWorkspaceService>(services);

    [HttpGet("students/{studentReference:guid}")]
    public async Task<IActionResult> Get(Guid studentReference, CancellationToken ct)
    {
        var result = await _service.GetAsync(studentReference, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStudentGuardianWorkspaceRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.CreateAndLinkAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("existing")]
    public async Task<IActionResult> LinkExisting([FromBody] LinkExistingStudentGuardianRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.LinkExistingAsync(request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("students/{studentReference:guid}/links/{linkId:long}/unlink")]
    public async Task<IActionResult> Unlink(Guid studentReference, long linkId,
        [FromBody] UnlinkStudentGuardianWorkspaceRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.UnlinkAsync(studentReference, linkId, request, ct);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPut("students/{studentReference:guid}/links/{linkId:long}")]
    public async Task<IActionResult> Update(Guid studentReference, long linkId,
        [FromBody] UpdateStudentGuardianWorkspaceLinkDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var result = await _service.UpdateLinkAsync(studentReference, linkId, request, ct);
        return StatusCode(result.StatusCode, result);
    }
}
