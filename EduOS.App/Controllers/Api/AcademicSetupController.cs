using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-setup")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AcademicSetupController : ControllerBase
{
    private readonly IAcademicSetupService _service;

    public AcademicSetupController(IAcademicSetupService service) => _service = service;

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog([FromQuery] long? academicYearId, CancellationToken cancellationToken) =>
        ToAction(await _service.GetCatalogAsync(academicYearId, cancellationToken));

    [HttpPost("programmes")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateProgram([FromBody] CreateAcademicProgramDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateProgramAsync(request, cancellationToken));

    [HttpPost("programmes/{academicProgramId:long}/levels")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateLevel(long academicProgramId, [FromBody] CreateAcademicLevelDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateLevelAsync(academicProgramId, request, cancellationToken));

    [HttpPost("tracks")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateTrack([FromBody] CreateAcademicTrackDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateTrackAsync(request, cancellationToken));

    [HttpPost("subjects")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateSubject([FromBody] CreateAcademicSubjectDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateSubjectAsync(request, cancellationToken));

    [HttpPost("curricula")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateCurriculum([FromBody] CreateAcademicCurriculumDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateCurriculumAsync(request, cancellationToken));

    [HttpPost("curricula/{academicCurriculumId:long}/subjects")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> RegisterCurriculumSubject(long academicCurriculumId, [FromBody] RegisterCurriculumSubjectDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.RegisterCurriculumSubjectAsync(academicCurriculumId, request, cancellationToken));

    [HttpPost("batches")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateBatch([FromBody] CreateAcademicBatchDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateBatchAsync(request, cancellationToken));

    [HttpPost("rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateAcademicRoomDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateRoomAsync(request, cancellationToken));

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
