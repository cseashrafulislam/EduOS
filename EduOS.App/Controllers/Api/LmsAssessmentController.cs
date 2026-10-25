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
[Route("api/lms/quizzes")]
[Authorize(Roles = "TenantAdmin,Principal,Teacher,Student")]
[RequireModule("LMS")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LmsAssessmentController : ControllerBase
{
    private readonly ILmsAssessmentService _service;
    // Platform owns shared DI; instantiate with already registered repositories.
    public LmsAssessmentController(IServiceProvider provider) =>
        _service = ActivatorUtilities.CreateInstance<LmsAssessmentService>(provider);
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);

    [HttpGet("course/{courseReference:guid}/author")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> AuthorList(Guid courseReference, CancellationToken ct) =>
        Reply(await _service.GetCourseQuizzesAsync(courseReference, ct));

    [HttpGet("course/{courseReference:guid}/available")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Available(Guid courseReference, CancellationToken ct) =>
        Reply(await _service.GetAvailableQuizzesAsync(courseReference, ct));

    [HttpGet("course/{courseReference:guid}/my-enrollment")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> MyEnrollment(Guid courseReference, CancellationToken ct) =>
        Reply(await _service.GetMyCourseEnrollmentIdAsync(courseReference, ct));

    [HttpGet("course/{courseReference:guid}/eligibility")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Eligibility(Guid courseReference, CancellationToken ct) =>
        Reply(await _service.GetMyQuizAttemptEligibilityAsync(courseReference, ct));

    [HttpGet("course/{courseReference:guid}/history")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> History(Guid courseReference, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Reply(await _service.GetMyQuizHistoryAsync(courseReference, page, pageSize, ct));

    [HttpGet("{reference:guid}/author")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> AuthorQuiz(Guid reference, CancellationToken ct) =>
        Reply(await _service.GetQuizForAuthorAsync(reference, ct));

    [HttpGet("{reference:guid}/candidate")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Candidate(Guid reference, CancellationToken ct) =>
        Reply(await _service.GetQuizForLearnerAsync(reference, ct));

    [HttpPost]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Create([FromBody] SaveQuizRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.SaveQuizAsync(null, request, ct));
    }

    [HttpPut("{reference:guid}")]
    [Authorize(Roles = "TenantAdmin,Principal,Teacher")]
    public async Task<IActionResult> Update(Guid reference, [FromBody] SaveQuizRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.SaveQuizAsync(reference, request, ct));
    }

    [HttpPost("attempts")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Start([FromBody] StartQuizAttemptRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.StartQuizAttemptAsync(request, ct));
    }

    [HttpPost("attempts/{id:long}/submit")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Submit(long id, [FromBody] SubmitQuizAttemptRequestDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await _service.SubmitQuizAttemptAsync(id, request, ct));
    }

    [HttpGet("attempts/{id:long}")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Attempt(long id, CancellationToken ct) =>
        Reply(await _service.GetMyAttemptAsync(id, ct));

    [HttpGet("attempts/{id:long}/review")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Review(long id, CancellationToken ct) =>
        Reply(await _service.GetMyAttemptReviewAsync(id, ct));
}
