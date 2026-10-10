using EduOS.Core.DTOs.SaaS;
using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ApiController]
[Route("api/institution-onboarding")]
public sealed class InstitutionOnboardingController : ControllerBase
{
    private readonly IInstitutionRegistrationService _registration;
    private readonly IInstitutionFoundationService _foundation;
    private readonly IInstitutionProfileWizardService _profile;
    private readonly IOnboardingService _onboarding;

    public InstitutionOnboardingController(IInstitutionRegistrationService registration,
        IInstitutionFoundationService foundation, IInstitutionProfileWizardService profile,
        IOnboardingService onboarding)
    {
        _registration = registration; _foundation = foundation;
        _profile = profile; _onboarding = onboarding;
    }

    [AllowAnonymous]
    [EnableRateLimiting("SignupPolicy")]
    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] InstitutionSignupRequestDto request, CancellationToken ct) =>
        Result(await _registration.RegisterInstitutionAsync(request, ct));

    [AllowAnonymous]
    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token,
        CancellationToken ct)
    {
        var checkedToken = await _registration.VerifyEmailAsync(email, token, ct);
        return Redirect(checkedToken.Success && checkedToken.Data
            ? "/Account/VerifyEmailSuccess" : "/Account/VerifyFailed");
    }

    [HttpGet("institution-profile")]
    public async Task<IActionResult> Profile(CancellationToken ct) =>
        Result(await _profile.GetAsync(ct));

    [HttpPost("institution-profile")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SaveProfile([FromForm] InstitutionProfileWizardDto request,
        CancellationToken ct) => Result(await _profile.SaveAsync(request, ct));

    [HttpGet("campus-list")]
    public async Task<IActionResult> Campuses(CancellationToken ct) =>
        Result(await _foundation.GetCampusesAsync(ct));

    [HttpGet("campus/{id:long}")]
    public async Task<IActionResult> Campus(long id, CancellationToken ct) =>
        Result(await _foundation.GetCampusAsync(id, ct));

    [HttpPost("campus")]
    public async Task<IActionResult> SaveCampus([FromBody] InstitutionCampusWizardRequestDto request,
        CancellationToken ct)
    {
        if (request == null) return BadRequest("Campus details are required.");
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            var existing = request.Id.HasValue ? await _foundation.GetCampusAsync(request.Id.Value, ct) : null;
            if (existing != null && !existing.Success) return Result(existing);
            request.Code = existing?.Data?.Code ?? "C-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        }
        return Result(await _foundation.SaveCampusAsync(request.Id, request, ct));
    }

    [HttpDelete("campus/{id:long}")]
    public async Task<IActionResult> ArchiveCampus(long id, [FromQuery] string rowVersion,
        CancellationToken ct) =>
        Result(await _foundation.ArchiveCampusAsync(id, rowVersion, ct));

    [HttpGet("academic-years")]
    public async Task<IActionResult> AcademicYears(CancellationToken ct) =>
        Result(await _foundation.GetAcademicYearsAsync(ct));

    [HttpGet("academic-year/{id:long}")]
    public async Task<IActionResult> AcademicYear(long id, CancellationToken ct) =>
        Result(await _foundation.GetAcademicYearAsync(id, ct));

    [HttpPost("academic-year")]
    public async Task<IActionResult> SaveAcademicYear(
        [FromBody] InstitutionAcademicYearWizardRequestDto request, CancellationToken ct)
    {
        if (request == null) return BadRequest("Academic year is required.");
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            var existing = request.Id.HasValue ? await _foundation.GetAcademicYearAsync(request.Id.Value, ct) : null;
            if (existing != null && !existing.Success) return Result(existing);
            request.Code = existing?.Data?.Code ?? "AY-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        }
        return Result(await _foundation.SaveAcademicYearAsync(request.Id, request, ct));
    }

    [HttpDelete("academic-year/{id:long}")]
    public async Task<IActionResult> ArchiveAcademicYear(long id, [FromQuery] string rowVersion,
        CancellationToken ct) =>
        Result(await _foundation.ArchiveAcademicYearAsync(id, rowVersion, ct));

    [HttpGet("academic-terms")]
    public async Task<IActionResult> AcademicTerms([FromQuery] long? academicYearId,
        CancellationToken ct) =>
        Result(await _foundation.GetAcademicTermsAsync(academicYearId, ct));

    [HttpGet("academic-term/{id:long}")]
    public async Task<IActionResult> AcademicTerm(long id, CancellationToken ct) =>
        Result(await _foundation.GetAcademicTermAsync(id, ct));

    [HttpPost("academic-term")]
    public async Task<IActionResult> SaveAcademicTerm(
        [FromBody] InstitutionAcademicTermWizardRequestDto request, CancellationToken ct) =>
        Result(await _foundation.SaveAcademicTermAsync(request.Id, request, ct));

    [HttpDelete("academic-term/{id:long}")]
    public async Task<IActionResult> ArchiveAcademicTerm(long id, [FromQuery] string rowVersion,
        CancellationToken ct) =>
        Result(await _foundation.ArchiveAcademicTermAsync(id, rowVersion, ct));

    [HttpPost("final-complete")]
    public async Task<IActionResult> FinalComplete(CancellationToken ct) =>
        Result(await _onboarding.CompleteOnboardingAsync(ct));

    private IActionResult Result<T>(EduOS.Core.Common.ApiResponse<T> result) =>
        StatusCode(result.StatusCode, result);
}
