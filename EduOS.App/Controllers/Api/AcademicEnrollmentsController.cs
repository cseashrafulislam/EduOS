using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-enrollments")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Student,Guardian,Parent")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AcademicEnrollmentsController : ControllerBase
{
    private readonly IAcademicEnrollmentService _service;

    public AcademicEnrollmentsController(IAcademicEnrollmentService service) => _service = service;

    [HttpGet("students/{studentReference:guid}/current")]
    public async Task<IActionResult> Current(Guid studentReference, CancellationToken cancellationToken) =>
        ToAction(await _service.GetCurrentAsync(studentReference, cancellationToken));

    [HttpGet("students/{studentReference:guid}/timetable")]
    public async Task<IActionResult> Timetable(Guid studentReference, CancellationToken cancellationToken) =>
        ToAction(await _service.GetTimetableAsync(studentReference, cancellationToken));

    [HttpPost]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> Enroll([FromBody] CreateAcademicStudentEnrollmentDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.EnrollAsync(request, cancellationToken));

    [HttpPost("{studentEnrollmentId:long}/subjects/requests")]
    [Authorize(Roles = "Student,Guardian,Parent")]
    public async Task<IActionResult> RequestOptionalSubject(long studentEnrollmentId, [FromBody] RequestOptionalSubjectDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.RequestOptionalSubjectAsync(studentEnrollmentId, request, cancellationToken));

    [HttpPost("subjects/{registrationId:long}/decision")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> DecideSubject(long registrationId, [FromBody] DecideSubjectRegistrationDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.DecideSubjectAsync(registrationId, request, cancellationToken));

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
