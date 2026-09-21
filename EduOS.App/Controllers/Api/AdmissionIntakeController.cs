using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,AdmissionOfficer")]
[RequireModule("ADMISSION")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("AdmissionIntakePolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/admission-intake")]
public sealed class AdmissionIntakeController : ControllerBase
{
    private readonly IAdmissionIntakeService _service;

    public AdmissionIntakeController(IAdmissionIntakeService service) => _service = service;

    [HttpGet("forms")]
    public async Task<IActionResult> Forms(CancellationToken cancellationToken) =>
        ToAction(await _service.GetFormsAsync(cancellationToken));

    [HttpPost("forms")]
    public async Task<IActionResult> CreateForm([FromBody] CreateAdmissionIntakeFormDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CreateFormAsync(request, cancellationToken));

    [HttpPut("forms/{id:long}")]
    public async Task<IActionResult> UpdateForm(long id, [FromBody] UpdateAdmissionIntakeFormDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.UpdateFormAsync(id, request, cancellationToken));

    [HttpPost("forms/{id:long}/publish")]
    public async Task<IActionResult> PublishForm(long id, [FromBody] AdmissionRowVersionDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.PublishFormAsync(id, request, cancellationToken));

    [HttpPost("forms/{id:long}/close")]
    public async Task<IActionResult> CloseForm(long id, [FromBody] AdmissionRowVersionDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.CloseFormAsync(id, request, cancellationToken));

    [HttpGet("applications/{reference:guid}/documents")]
    public async Task<IActionResult> Documents(Guid reference, CancellationToken cancellationToken) =>
        ToAction(await _service.GetDocumentsAsync(reference, cancellationToken));

    [HttpPost("applications/{reference:guid}/documents/{documentId:long}/review")]
    public async Task<IActionResult> ReviewDocument(Guid reference, long documentId, [FromBody] ReviewAdmissionDocumentDto request, CancellationToken cancellationToken) =>
        ToAction(await _service.ReviewDocumentAsync(reference, documentId, request, cancellationToken));

    [HttpGet("applications/{reference:guid}/documents/{documentId:long}/content")]
    public async Task<IActionResult> DocumentContent(Guid reference, long documentId, CancellationToken cancellationToken)
    {
        var response = await _service.GetDocumentContentAsync(reference, documentId, cancellationToken);
        return response.Success && response.Data != null
            ? File(response.Data.Content, response.Data.ContentType, response.Data.FileName)
            : StatusCode(response.StatusCode, response);
    }

    private IActionResult ToAction<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
