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

[ApiController, Route("api/research/proposals"), Authorize, RequireModule("RESEARCH"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResearchProposalsApiController(IResearchProposalService service) : ControllerBase
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
}
