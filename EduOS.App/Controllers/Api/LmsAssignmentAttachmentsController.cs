using EduOS.App.Authorization;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/lms/assignment-attachments")]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsAssignmentAttachmentsController : ControllerBase
{
    private readonly ILmsCourseContentService _service;
    public LmsAssignmentAttachmentsController(ILmsCourseContentService implementation) => _service = implementation;

    [HttpGet("{assignmentReference:guid}")]
    public async Task<IActionResult> List(Guid assignmentReference, CancellationToken ct)
    {
        var response = await _service.GetAssignmentAttachmentsAsync(assignmentReference, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Create([FromBody] SaveAssignmentAttachmentRequestDto request, CancellationToken ct)
    {
        var response = await _service.SaveAssignmentAttachmentAsync(null, request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveAssignmentAttachmentRequestDto request, CancellationToken ct)
    {
        var response = await _service.SaveAssignmentAttachmentAsync(id, request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("{id:long}/remove")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Remove(long id, [FromBody] RemoveAssignmentAttachmentRequestDto request, CancellationToken ct)
    {
        var response = await _service.RemoveAssignmentAttachmentAsync(id, request.RowVersion, ct);
        return StatusCode(response.StatusCode, response);
    }
}
