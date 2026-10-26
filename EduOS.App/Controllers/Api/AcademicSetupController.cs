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
    public async Task<IActionResult> Catalog([FromQuery] long? academicYearId, [FromQuery] string? search,
        [FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetSetupOptionsAsync(academicYearId, search, take, cancellationToken));

    [HttpGet("programmes/{academicProgramId:long}/campuses")]
    public async Task<IActionResult> ProgrammeCampuses(long academicProgramId, CancellationToken cancellationToken) =>
        ToAction(await _service.GetProgramCampusAllocationAsync(academicProgramId, cancellationToken));

    [HttpPut("programmes/{academicProgramId:long}/campuses")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateProgrammeCampuses(long academicProgramId,
        [FromBody] UpdateProgramCampusAllocationRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateProgramCampusAllocationAsync(academicProgramId, request, cancellationToken));

    [HttpGet("departments")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Departments([FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetDepartmentsAsync(search, page, pageSize, cancellationToken));

    [HttpPost("departments")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CreateDepartment([FromBody] SaveAcademicDepartmentRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.CreateDepartmentAsync(request, cancellationToken));

    [HttpPut("departments/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateDepartment(long id, [FromBody] SaveAcademicDepartmentRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateDepartmentAsync(id, request, cancellationToken));

    [HttpPost("programmes")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateProgram([FromBody] SaveAcademicProgramRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateProgramAsync(request, cancellationToken));

    [HttpGet("programmes")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Programmes([FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetProgramsAsync(search, page, pageSize, cancellationToken));

    [HttpGet("programmes/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Programme(long id, CancellationToken cancellationToken) =>
        ToAction(await _service.GetProgramAsync(id, cancellationToken));

    [HttpPut("programmes/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateProgramme(long id, [FromBody] UpdateAcademicProgramRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateProgramAsync(id, request, cancellationToken));

    [HttpPost("programmes/{academicProgramId:long}/levels")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateLevel(long academicProgramId, [FromBody] SaveAcademicLevelRequestDto request, CancellationToken cancellationToken)
    {
        if (academicProgramId <= 0 || request == null || request.AcademicProgramId != academicProgramId)
            return BadRequest("Academic program in the URL must match the request.");
        return ToAction(await _service.CreateLevelAsync(request, cancellationToken));
    }

    [HttpPost("levels")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateLevelCanonical([FromBody] SaveAcademicLevelRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateLevelAsync(request, cancellationToken));

    [HttpGet("levels")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Levels([FromQuery] string? search, [FromQuery] long? academicProgramId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetLevelsAsync(search, academicProgramId, page, pageSize, cancellationToken));

    [HttpGet("levels/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Level(long id, CancellationToken cancellationToken) =>
        ToAction(await _service.GetLevelAsync(id, cancellationToken));

    [HttpPut("levels/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateLevel(long id, [FromBody] UpdateAcademicLevelRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateLevelAsync(id, request, cancellationToken));

    [HttpPost("tracks")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateTrack([FromBody] SaveAcademicTrackRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateTrackAsync(request, cancellationToken));

    [HttpPost("subjects")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateSubject([FromBody] SaveSubjectRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateSubjectAsync(request, cancellationToken));

    [HttpPost("curricula")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateCurriculum([FromBody] SaveAcademicCurriculumRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateCurriculumAsync(request, cancellationToken));

    [HttpPut("curricula/{academicCurriculumId:long}/activate")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> ActivateCurriculum(long academicCurriculumId,
        [FromQuery] string rowVersion, CancellationToken cancellationToken) =>
        ToAction(await _service.ActivateCurriculumAsync(academicCurriculumId, rowVersion, cancellationToken));

    [HttpPost("curricula/{academicCurriculumId:long}/subjects")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> RegisterCurriculumSubject(long academicCurriculumId, [FromBody] SaveCurriculumSubjectRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.RegisterCurriculumSubjectAsync(academicCurriculumId, request, cancellationToken));

    [HttpPost("batches")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateBatch([FromBody] SaveAcademicBatchRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateBatchAsync(request, cancellationToken));

    [HttpGet("batches")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Batches([FromQuery] string? search, [FromQuery] long? campusId,
        [FromQuery] long? academicYearId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetBatchesAsync(search, campusId, academicYearId, page, pageSize, cancellationToken));

    [HttpGet("batches/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Batch(long id, CancellationToken cancellationToken) =>
        ToAction(await _service.GetBatchAsync(id, cancellationToken));

    [HttpPut("batches/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateBatch(long id, [FromBody] UpdateAcademicBatchRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateBatchAsync(id, request, cancellationToken));

    [HttpGet("batches/{academicBatchId:long}/offerings")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Offerings(long academicBatchId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetSubjectOfferingsAsync(academicBatchId, search, page, pageSize, cancellationToken));

    [HttpPost("offerings")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CreateOffering([FromBody] SaveSubjectOfferingRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.CreateSubjectOfferingAsync(request, cancellationToken));

    [HttpPut("offerings/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateOffering(Guid reference, [FromBody] UpdateSubjectOfferingRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateSubjectOfferingAsync(reference, request, cancellationToken));

    [HttpPost("rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateRoom([FromBody] SaveRoomRequestDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateRoomAsync(request, cancellationToken));

    [HttpGet("rooms")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Rooms([FromQuery] string? search, [FromQuery] long? campusId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        ToAction(await _service.GetRoomsAsync(search, campusId, page, pageSize, cancellationToken));

    [HttpGet("rooms/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Room(long id, CancellationToken cancellationToken) =>
        ToAction(await _service.GetRoomAsync(id, cancellationToken));

    [HttpPut("rooms/{id:long}")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateRoom(long id, [FromBody] UpdateAcademicRoomRequestDto request,
        CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateRoomAsync(id, request, cancellationToken));

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
