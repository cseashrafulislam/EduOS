using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-routines")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AcademicRoutinesController : ControllerBase
{
    private readonly IAcademicRoutineService _service;
    public AcademicRoutinesController(IAcademicRoutineService service) => _service = service;

    [HttpGet("time-slots")]
    public async Task<IActionResult> TimeSlots(CancellationToken cancellationToken) => ToAction(await _service.GetTimeSlotsAsync(cancellationToken));

    [HttpPost("time-slots")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateTimeSlot([FromBody] CreateRoutineTimeSlotDto request, CancellationToken cancellationToken) => ToAction(await _service.CreateTimeSlotAsync(request, cancellationToken));

    [HttpGet("batches/{academicBatchId:long}/assignments")]
    public async Task<IActionResult> Assignments(long academicBatchId, [FromQuery] long? academicTermId, CancellationToken cancellationToken) => ToAction(await _service.GetAssignmentsAsync(academicBatchId, academicTermId, cancellationToken));

    [HttpPost("assignments")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> AssignInstructor([FromBody] AssignInstructorDto request, CancellationToken cancellationToken) => ToAction(await _service.AssignInstructorAsync(request, cancellationToken));

    [HttpGet("batches/{academicBatchId:long}")]
    public async Task<IActionResult> BatchTimetable(long academicBatchId, [FromQuery] long? academicTermId, CancellationToken cancellationToken) => ToAction(await _service.GetBatchTimetableAsync(academicBatchId, academicTermId, cancellationToken));

    [HttpGet("teachers/{employeeId:long}")]
    public async Task<IActionResult> TeacherTimetable(long employeeId, [FromQuery] long academicYearId, [FromQuery] long? academicTermId, CancellationToken cancellationToken) => ToAction(await _service.GetTeacherTimetableAsync(employeeId, academicYearId, academicTermId, cancellationToken));

    [HttpPost("entries")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateEntry([FromBody] CreateRoutineEntryDto request, CancellationToken cancellationToken) => ToAction(await _service.CreateEntryAsync(request, cancellationToken));

    [HttpPost("entries/{id:long}/deactivate")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> DeactivateEntry(long id, CancellationToken cancellationToken) => ToAction(await _service.DeactivateEntryAsync(id, cancellationToken));

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
