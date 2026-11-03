using System.Security.Claims;
using EduOS.App.Authorization;
using EduOS.App.Security;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Research;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/research/proposals"), Authorize, RequireModule("RESEARCH"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResearchProposalsApiController(IResearchProposalService service,
    IResearchEvidenceFileService evidence) : ControllerBase
{
    private bool BindSession()
    {
        var sid = User.FindFirstValue(AuthSessionRegistry.ClaimType);
        var tenant = User.FindFirstValue("TenantId");
        if (sid is not { Length: 48 } || !long.TryParse(tenant, out var tenantId) || tenantId <= 0)
            return false;
        Response.Headers["X-EduOS-Session-Reference"] = AuthSessionRegistry.ToPublicReference(sid);
        Response.Headers["X-EduOS-Tenant-Id"] = tenant;
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return true;
    }

    private IActionResult Reply<T>(ApiResponse<T> result) => BindSession()
        ? StatusCode(result.StatusCode, result)
        : Unauthorized(new { success = false, message = "Session unavailable; sign in again." });

    [HttpGet]
    public async Task<IActionResult> List(long? campusId, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.ListAsync(campusId, page, pageSize, ct));

    [HttpGet("campuses")]
    public async Task<IActionResult> Campuses(CancellationToken ct) => Reply(await service.CampusesAsync(ct));

    [HttpGet("programs")]
    public async Task<IActionResult> Programs(long campusId, CancellationToken ct) =>
        Reply(await service.ProgramsAsync(campusId, ct));

    [HttpGet("reviewers")]
    public async Task<IActionResult> Reviewers(long campusId, string? search, CancellationToken ct) =>
        Reply(await service.ReviewersAsync(campusId, search, ct));

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct) =>
        Reply(await service.DetailAsync(reference, ct));

    [HttpGet("{reference:guid}/history")]
    public async Task<IActionResult> History(Guid reference, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.HistoryAsync(reference, page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ResearchProposalCreateDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await service.CreateAsync(request, ct));
    }

    [HttpPost("{reference:guid}/{action}")]
    public async Task<IActionResult> Command(Guid reference, string action,
        [FromBody] ResearchProposalCommandDto request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Reply(await service.CommandAsync(reference, action, request, ct));
    }

    [HttpGet("{reference:guid}/files")]
    public async Task<IActionResult> Files(Guid reference, int page = 1, int pageSize = 25,
        CancellationToken ct = default) =>
        Reply(await evidence.ListAsync("proposal", reference, page, pageSize, ct));

    [HttpPost("{reference:guid}/files")]
    [Consumes("multipart/form-data"), RequestSizeLimit(6291456)]
    public async Task<IActionResult> UploadFile(Guid reference, [FromForm] IFormFile? file,
        [FromForm] Guid requestId, CancellationToken ct)
    {
        if (file == null || requestId == Guid.Empty || file.Length is < 1 or > 5242880)
            return BadRequest(new { success = false, message = "Choose a file up to 5 MB and a unique request ID." });
        await using var stream = file.OpenReadStream();
        return Reply(await evidence.UploadAsync("proposal", reference, requestId, new PrivateFileUploadDto
        { FileName = file.FileName, ContentType = file.ContentType, Length = file.Length, Content = stream }, ct));
    }

    [HttpGet("{reference:guid}/files/{fileReference:guid}/download")]
    public async Task<IActionResult> DownloadFile(Guid reference, Guid fileReference, CancellationToken ct)
    {
        if (!BindSession()) return Unauthorized();
        var result = await evidence.DownloadAsync("proposal", reference, fileReference, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode, result);
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
