using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Security;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/accreditation"), Authorize, RequireModule("ACCREDITATION"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResearchQualityAccreditationApiController(IResearchQualityExtensionService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> result)
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenant = User.FindFirstValue("TenantId");
        if (sid is not { Length: 48 } || !long.TryParse(tenant, out var id) || id <= 0)
            return Unauthorized(new { success = false, message = "Session unavailable." });
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = tenant;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("program-outcomes")]
    public async Task<IActionResult> Outcomes(long campusId, long programId, CancellationToken ct) =>
        Reply(await service.OutcomesAsync(campusId, programId, ct));

    [HttpPost("program-outcomes")]
    public async Task<IActionResult> CreateOutcome([FromBody] ProgramOutcomeCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CreateOutcomeAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpGet("standards/{standardId:long}/outcomes")]
    public async Task<IActionResult> StandardOutcomes(long standardId, CancellationToken ct) =>
        Reply(await service.StandardOutcomesAsync(standardId, ct));

    [HttpPost("standard-outcomes")]
    public async Task<IActionResult> MapOutcome([FromBody] StandardOutcomeMapCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.MapOutcomeAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpGet("assessments/{reference:guid}/academic-evidence")]
    public async Task<IActionResult> AcademicEvidence(Guid reference, CancellationToken ct) =>
        Reply(await service.AcademicEvidenceAsync(reference, ct));

    [HttpPost("assessments/{reference:guid}/academic-evidence")]
    public async Task<IActionResult> LinkEvidence(Guid reference,
        [FromBody] AcademicPublicationEvidenceLinkDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.LinkAcademicEvidenceAsync(reference, input, ct)) : ValidationProblem(ModelState);
}
