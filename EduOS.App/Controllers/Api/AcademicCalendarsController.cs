using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/academic-calendars")]
[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher,Student,Guardian,Parent")]
[RequireModule("ACADEMIC")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AcademicCalendarsController : ControllerBase
{
    private readonly IAcademicCalendarService _service;

    public AcademicCalendarsController(IAcademicCalendarService service) => _service = service;

    [HttpGet("policy")]
    public async Task<IActionResult> Policy([FromQuery] long academicYearId, [FromQuery] long? campusId, CancellationToken cancellationToken) =>
        ToAction(await _service.GetPolicyAsync(academicYearId, campusId, cancellationToken));

    [HttpPost("policy")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> SavePolicy([FromBody] SaveAcademicCalendarPolicyDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.SavePolicyAsync(request, cancellationToken));

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] long academicYearId, [FromQuery] long? campusId, [FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, CancellationToken cancellationToken) =>
        ToAction(await _service.GetEventsAsync(academicYearId, campusId, fromDate, toDate, cancellationToken));

    [HttpPost("events")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> CreateEvent([FromBody] CreateAcademicCalendarEventDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateEventAsync(request, cancellationToken));

    [HttpPost("events/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]
    public async Task<IActionResult> UpdateEvent(long id, [FromBody] UpdateAcademicCalendarEventDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateEventAsync(id, request, cancellationToken));

    [HttpGet("working-days")]
    public async Task<IActionResult> WorkingDays([FromQuery] long academicYearId, [FromQuery] long? campusId, [FromQuery] DateTime fromDate, [FromQuery] DateTime toDate, CancellationToken cancellationToken) =>
        ToAction(await _service.GetWorkingDaysAsync(academicYearId, campusId, fromDate, toDate, cancellationToken));

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
