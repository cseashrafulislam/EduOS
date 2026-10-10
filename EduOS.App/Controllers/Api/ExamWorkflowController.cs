using EduOS.App.Authorization;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,Teacher,ExamController")]
[RequireModule("EXAM")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/exams/workflow")]
public sealed class ExamWorkflowController : ControllerBase
{
    private readonly IAssessmentAdministrationService _service;
    public ExamWorkflowController(IAssessmentAdministrationService service) => _service = service;

    [HttpGet("scopes")]
    public async Task<IActionResult> Scopes(CancellationToken ct) =>
        Result(await _service.GetAvailableScopesAsync(ct));

    [HttpGet("mark-roster")]
    public async Task<IActionResult> MarkRoster([FromQuery] AssessmentMarkRosterQueryDto query,
        CancellationToken ct) => Result(await _service.GetMarkRosterAsync(query, ct));

    [HttpPost("marks")]
    public async Task<IActionResult> SaveMarks([FromBody] SaveMarksRegisterRequestDto request,
        CancellationToken ct) => Result(await _service.SaveMarksRegisterAsync(request, ct));

    [HttpPost("results/generate")]
    [HttpPost("results/preview")]
    public async Task<IActionResult> Preview([FromBody] AssessmentScopeDto request,
        CancellationToken ct) => Result(await _service.PreviewResultsAsync(request, ct));

    [HttpGet("results")]
    public async Task<IActionResult> Results([FromQuery] AssessmentScopeDto query,
        CancellationToken ct) => Result(await _service.GetResultsAsync(query, ct));

    [HttpPost("results/publish")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> Publish([FromBody] PublishResultRequestDto request,
        CancellationToken ct) => Result(await _service.PublishResultAsync(request, ct));

    [HttpPost("results/publications/{id:long}/withdraw")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> Withdraw(long id,
        [FromBody] WithdrawResultPublicationRequestDto request, CancellationToken ct) =>
        Result(await _service.WithdrawResultPublicationAsync(id, request, ct));

    [HttpPost("assessments")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> CreateAssessment([FromBody] SaveAssessmentRequestDto request,
        CancellationToken ct) => Result(await _service.SaveAssessmentAsync(null, request, ct));

    [HttpPut("assessments/{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> UpdateAssessment(Guid reference,
        [FromBody] SaveAssessmentRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveAssessmentAsync(reference, request, ct));

    [HttpGet("assessments/{reference:guid}")]
    public async Task<IActionResult> Assessment(Guid reference, CancellationToken ct) =>
        Result(await _service.GetAssessmentAsync(reference, ct));

    [HttpPost("assessments/{reference:guid}/state")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> ChangeState(Guid reference,
        [FromBody] ChangeAssessmentStateRequestDto request, CancellationToken ct) =>
        Result(await _service.ChangeAssessmentStateAsync(reference, request, ct));

    [HttpPost("subjects")]
    [HttpPut("subjects/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveSubject(long? id, [FromBody] SaveAssessmentSubjectRequestDto request,
        CancellationToken ct) => Result(await _service.SaveAssessmentSubjectAsync(id, request, ct));

    [HttpPost("schedules")]
    [HttpPut("schedules/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveSchedule(long? id, [FromBody] SaveAssessmentScheduleRequestDto request,
        CancellationToken ct) => Result(await _service.SaveAssessmentScheduleAsync(id, request, ct));

    [HttpPost("grade-schemes")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveGradeScheme([FromBody] SaveGradeSchemeRequestDto request,
        CancellationToken ct) => Result(await _service.SaveGradeSchemeAsync(null, request, ct));

    [HttpPost("components")]
    [HttpPut("components/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveComponent(long? id, [FromBody] SaveAssessmentComponentRequestDto request,
        CancellationToken ct) => Result(await _service.SaveAssessmentComponentAsync(id, request, ct));

    [HttpPost("component-marks")]
    [HttpPut("component-marks/{id:long}")]
    public async Task<IActionResult> SaveComponentMark(long? id,
        [FromBody] SaveStudentAssessmentComponentMarkRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveComponentMarkAsync(id, request, ct));

    [HttpPost("certificate-templates")]
    [HttpPut("certificate-templates/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveCertificateTemplate(long? id,
        [FromBody] SaveCertificateTemplateRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveCertificateTemplateAsync(id, request, ct));

    [HttpPost("certificates")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> IssueCertificate([FromBody] IssueCertificateRequestDto request,
        CancellationToken ct) => Result(await _service.IssueCertificateAsync(request, ct));

    [HttpPost("certificates/{id:long}/revoke")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> RevokeCertificate(long id,
        [FromBody] RevokeCertificateRequestDto request, CancellationToken ct) =>
        Result(await _service.RevokeCertificateAsync(id, request, ct));

    [HttpPost("transcripts")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> IssueTranscript([FromBody] IssueTranscriptRequestDto request,
        CancellationToken ct) => Result(await _service.IssueTranscriptAsync(request, ct));

    [HttpPost("transcripts/{reference:guid}/revoke")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> RevokeTranscript(Guid reference,
        [FromBody] RevokeTranscriptRequestDto request, CancellationToken ct) =>
        Result(await _service.RevokeTranscriptAsync(reference, request, ct));

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> result) =>
        StatusCode(result.StatusCode, result);
}
