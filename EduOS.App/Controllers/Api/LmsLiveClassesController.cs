using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/live-classes")]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsLiveClassesController : ControllerBase
{
    private readonly ILmsLiveClassService _service;

    // Platform owns shared service registrations. Resolve through the registered repositories.
    public LmsLiveClassesController(IServiceProvider provider) =>
        _service = provider.GetService<ILmsLiveClassService>() ??
            ActivatorUtilities.CreateInstance<LmsLiveClassService>(provider);

    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);

    [HttpGet("course/{courseReference:guid}")]
    public async Task<IActionResult> Course(Guid courseReference, CancellationToken ct) =>
        Reply(await _service.GetCourseLiveClassesAsync(courseReference, ct));

    [HttpGet("recording-courses")]
    public async Task<IActionResult> RecordingCourses([FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Reply(await _service.GetRecordingCoursesAsync(page, pageSize, ct));

    [HttpGet("course/{courseReference:guid}/recordings")]
    public async Task<IActionResult> Recordings(Guid courseReference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Reply(await _service.GetCourseRecordingsAsync(courseReference, page, pageSize, ct));

    [HttpGet("course/{courseReference:guid}/pending-recordings")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> PendingRecordings(Guid courseReference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Reply(await _service.GetPendingRecordingsAsync(courseReference, page, pageSize, ct));

    [HttpPost("{reference:guid}/recording")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> PublishRecording(Guid reference,
        [FromBody] PublishLiveClassRecordingRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.PublishRecordingAsync(reference, request, ct));
    }

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Details(Guid reference, CancellationToken ct) =>
        Reply(await _service.GetLiveClassAsync(reference, ct));

    [HttpPost]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Create([FromBody] SaveLiveClassRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.SaveLiveClassAsync(null, request, ct));
    }

    [HttpPut("{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Update(Guid reference, [FromBody] SaveLiveClassRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.SaveLiveClassAsync(reference, request, ct));
    }

    [HttpPost("{reference:guid}/cancel")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Cancel(Guid reference, [FromBody] CancelLiveClassRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.CancelLiveClassAsync(reference, request.RowVersion, ct));
    }

    public sealed class CancelLiveClassRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string RowVersion { get; set; } = string.Empty;
    }
}
