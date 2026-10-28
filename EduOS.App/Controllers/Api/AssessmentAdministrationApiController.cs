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
[Route("api/assessment-administration")]
public sealed class AssessmentAdministrationApiController : ControllerBase
{
    private readonly IAssessmentAdministrationService _service;
    public AssessmentAdministrationApiController(IAssessmentAdministrationService service) => _service = service;

    [HttpGet("scopes")]
    public async Task<IActionResult> Scopes(CancellationToken ct) =>
        Result(await _service.GetAvailableScopesAsync(ct));

    [HttpGet("scopes/page")]
    public async Task<IActionResult> SearchScopes([FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        Result(await _service.SearchAvailableScopesAsync(search, page, pageSize, ct));

    [HttpGet("admit-cards")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> AdmitCards([FromQuery] AssessmentAdmitCardQueryDto query,
        CancellationToken ct) => Result(await _service.SearchAdmitCardsAsync(query, ct));

    [HttpGet("mark-roster/page")]
    public async Task<IActionResult> SearchMarkRoster([FromQuery] AssessmentMarkRosterPageQueryDto query,
        CancellationToken ct) => Result(await _service.SearchMarkRosterAsync(query, ct));

    [HttpGet("mark-roster")]
    public async Task<IActionResult> MarkRoster([FromQuery] AssessmentMarkRosterQueryDto query,
        CancellationToken ct) => Result(await _service.GetMarkRosterAsync(query, ct));

    [HttpPost("marks")]
    public async Task<IActionResult> SaveMarks([FromBody] SaveMarksRegisterRequestDto request,
        CancellationToken ct) => Result(await _service.SaveMarksRegisterAsync(request, ct));

    [HttpGet("marks/reviews/queue")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> MarkReviewQueue([FromQuery] AssessmentMarkReviewQueueQueryDto query,
        CancellationToken ct) => Result(await _service.SearchMarkReviewQueueAsync(query, ct));

    [HttpGet("marks/reviews/{subjectId:long}")]
    public async Task<IActionResult> MarkReview(long subjectId, CancellationToken ct) =>
        Result(await _service.GetMarkReviewAsync(subjectId, ct));

    [HttpGet("marks/reviews/{subjectId:long}/history")]
    public async Task<IActionResult> MarkReviewHistory(long subjectId,
        [FromQuery] AssessmentMarkReviewHistoryQueryDto query, CancellationToken ct) =>
        Result(await _service.SearchMarkReviewHistoryAsync(subjectId, query, ct));

    [HttpPost("marks/reviews/{subjectId:long}/decision")]
    public async Task<IActionResult> DecideMarkReview(long subjectId,
        [FromBody] AssessmentMarkReviewRequestDto request, CancellationToken ct) =>
        Result(await _service.DecideMarkReviewAsync(subjectId, request, ct));

    [HttpGet("marks/history")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> MarkChanges([FromQuery] AssessmentMarkChangeQueryDto query,
        CancellationToken ct) => Result(await _service.SearchMarkChangesAsync(query, ct));

    [HttpGet("results/readiness")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> ResultReadiness([FromQuery] AssessmentReadinessQueryDto query,
        CancellationToken ct) => Result(await _service.GetResultReadinessAsync(query, ct));

    [HttpPost("results/generate")]
    [HttpPost("results/preview")]
    public async Task<IActionResult> Preview([FromBody] AssessmentScopeDto request,
        CancellationToken ct) => Result(await _service.PreviewResultsAsync(request, ct));

    [HttpGet("results")]
    public async Task<IActionResult> Results([FromQuery] AssessmentScopeDto query,
        CancellationToken ct) => Result(await _service.GetResultsAsync(query, ct));

    [HttpGet("results/publications")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> PublicationHistory(
        [FromQuery] AssessmentPublicationHistoryQueryDto query, CancellationToken ct) =>
        Result(await _service.SearchPublicationHistoryAsync(query, ct));

    [HttpGet("results/tabulation")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> PublishedTabulation(
        [FromQuery] PublishedTabulationQueryDto query, CancellationToken ct) =>
        Result(await _service.SearchPublishedTabulationAsync(query, ct));

    [HttpGet("results/publications/{publicationId:long}/integrity")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> PublicationIntegrity(long publicationId,
        [FromQuery] PublishedSnapshotIntegrityQueryDto query, CancellationToken ct) =>
        Result(await _service.SearchPublishedSnapshotIssuesAsync(publicationId, query, ct));

    [HttpPost("results/publish")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> Publish([FromBody] PublishResultRequestDto request,
        CancellationToken ct) => Result(await _service.PublishResultAsync(request, ct));

    [HttpGet("subject-completions/batches/page")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SearchSubjectCompletionBatches(
        [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Result(await _service.SearchSubjectCompletionBatchesAsync(search, page, pageSize, ct));

    [HttpGet("subject-completions/batches")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SubjectCompletionBatches(CancellationToken ct) =>
        Result(await _service.GetSubjectCompletionBatchesAsync(ct));

    [HttpGet("subject-completions")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SubjectCompletions([FromQuery] SubjectCompletionQueryDto query,
        CancellationToken ct) => Result(await _service.SearchSubjectCompletionsAsync(query, ct));

    [HttpPost("subject-completions/{id:long}/complete")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> CompleteSubject(long id, [FromBody] CompleteSubjectRequestDto request,
        CancellationToken ct) => Result(await _service.CompleteSubjectAsync(id, request, ct));

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

    [HttpGet("schedules")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> ScheduleRegister([FromQuery] AssessmentScheduleRegisterQueryDto query,
        CancellationToken ct) => Result(await _service.SearchAssessmentSchedulesAsync(query, ct));

    [HttpGet("schedules/rooms")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> ScheduleRooms([FromQuery] long assessmentSubjectId,
        CancellationToken ct) => Result(await _service.GetAssessmentScheduleRoomsAsync(assessmentSubjectId, ct));

    [HttpPost("schedules")]
    [HttpPut("schedules/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveSchedule(long? id, [FromBody] SaveAssessmentScheduleRequestDto request,
        CancellationToken ct) => Result(await _service.SaveAssessmentScheduleAsync(id, request, ct));

    [HttpGet("grade-schemes")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> GradeSchemeRegister(
        [FromQuery] GradeSchemeRegisterQueryDto query, CancellationToken ct) =>
        Result(await _service.SearchGradeSchemesAsync(query, ct));

    [HttpGet("grade-schemes/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> GradeScheme(long id, CancellationToken ct) =>
        Result(await _service.GetGradeSchemeAsync(id, ct));

    [HttpPost("grade-schemes")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveGradeScheme([FromBody] SaveGradeSchemeRequestDto request,
        CancellationToken ct) => Result(await _service.SaveGradeSchemeAsync(null, request, ct));

    [HttpGet("components")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> ComponentRegister([FromQuery] AssessmentComponentRegisterQueryDto query,
        CancellationToken ct) => Result(await _service.SearchAssessmentComponentsAsync(query, ct));

    [HttpPost("components")]
    [HttpPut("components/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveComponent(long? id, [FromBody] SaveAssessmentComponentRequestDto request,
        CancellationToken ct) => Result(await _service.SaveAssessmentComponentAsync(id, request, ct));

    [HttpGet("component-marks/roster")]
    public async Task<IActionResult> ComponentMarkRoster([FromQuery] AssessmentComponentMarkRosterQueryDto query,
        CancellationToken ct) => Result(await _service.GetComponentMarkRosterAsync(query, ct));

    [HttpPost("component-marks")]
    [HttpPut("component-marks/{id:long}")]
    public async Task<IActionResult> SaveComponentMark(long? id,
        [FromBody] SaveStudentAssessmentComponentMarkRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveComponentMarkAsync(id, request, ct));

    [HttpGet("certificate-templates")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> CertificateTemplateRegister(
        [FromQuery] CertificateTemplateRegisterQueryDto query, CancellationToken ct) =>
        Result(await _service.SearchCertificateTemplatesAsync(query, ct));

    [HttpGet("certificate-templates/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> CertificateTemplate(long id, CancellationToken ct) =>
        Result(await _service.GetCertificateTemplateAsync(id, ct));

    [HttpPost("certificate-templates")]
    [HttpPut("certificate-templates/{id:long}")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SaveCertificateTemplate(long? id,
        [FromBody] SaveCertificateTemplateRequestDto request, CancellationToken ct) =>
        Result(await _service.SaveCertificateTemplateAsync(id, request, ct));

    [HttpGet("certificates/candidates/page")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SearchCertificateCandidates(
        [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Result(await _service.SearchCertificateCandidatesAsync(search, page, pageSize, ct));

    [HttpGet("certificates/candidates")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> CertificateCandidates([FromQuery] string? search, CancellationToken ct) =>
        Result(await _service.GetCertificateCandidatesAsync(search, ct));

    [HttpGet("certificates")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> CertificateRegister([FromQuery] CertificateIssueRegisterQueryDto query,
        CancellationToken ct) => Result(await _service.SearchCertificateIssueRegisterAsync(query, ct));

    [HttpPost("certificates")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> IssueCertificate([FromBody] IssueCertificateRequestDto request,
        CancellationToken ct) => Result(await _service.IssueCertificateAsync(request, ct));

    [HttpPost("certificates/{id:long}/revoke")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> RevokeCertificate(long id,
        [FromBody] RevokeCertificateRequestDto request, CancellationToken ct) =>
        Result(await _service.RevokeCertificateAsync(id, request, ct));

    [HttpGet("transcripts/candidates/page")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> SearchTranscriptCandidates(
        [FromQuery] string? search = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Result(await _service.SearchTranscriptCandidatesAsync(search, page, pageSize, ct));

    [HttpGet("transcripts/candidates")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> TranscriptCandidates([FromQuery] string? search, CancellationToken ct) =>
        Result(await _service.GetTranscriptCandidatesAsync(search, ct));

    [HttpGet("transcripts")]
    [Authorize(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController")]
    public async Task<IActionResult> TranscriptRegister([FromQuery] TranscriptRegisterQueryDto query, CancellationToken ct) =>
        Result(await _service.SearchTranscriptRegisterAsync(query, ct));

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
