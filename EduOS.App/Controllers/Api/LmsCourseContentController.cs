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
[Route("api/lms/course-content")]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsCourseContentController : ControllerBase
{
    private readonly ILmsCourseContentService _service;
    public LmsCourseContentController(ILmsCourseContentService implementation) => _service = implementation;

    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet("{courseReference:guid}")]
    public async Task<IActionResult> Course(Guid courseReference, CancellationToken ct) =>
        Reply(await _service.GetCourseContentAsync(courseReference, ct));

    [HttpPost("sections")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> CreateSection([FromBody] SaveCourseSectionRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveSectionAsync(null, request, ct));

    [HttpPut("sections/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> UpdateSection(long id, [FromBody] SaveCourseSectionRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveSectionAsync(id, request, ct));

    [HttpPost("lessons/section")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> AssignLesson([FromBody] AssignLessonSectionRequestDto request, CancellationToken ct) =>
        Reply(await _service.AssignLessonSectionAsync(request, ct));

    [HttpPost("resources")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> CreateResource([FromBody] SaveLessonResourceRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveResourceAsync(null, request, ct));

    [HttpPut("resources/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> UpdateResource(long id, [FromBody] SaveLessonResourceRequestDto request, CancellationToken ct) =>
        Reply(await _service.SaveResourceAsync(id, request, ct));

    [HttpPost("resources/{id:long}/remove")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> RemoveResource(long id, [FromBody] RemoveLessonResourceRequestDto request, CancellationToken ct) =>
        Reply(await _service.RemoveResourceAsync(id, request.RowVersion, ct));
}
