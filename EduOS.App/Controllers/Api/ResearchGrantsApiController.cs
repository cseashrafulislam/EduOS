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

[ApiController, Route("api/research/grants"), Authorize, RequireModule("RESEARCH"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResearchGrantsApiController(IResearchGrantService service) : ControllerBase
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

    [HttpGet]
    public async Task<IActionResult> List(long? campusId, int page = 1, int pageSize = 20,
        CancellationToken ct = default) => Reply(await service.ListAsync(campusId, page, pageSize, ct));

    [HttpGet("schemes")]
    public async Task<IActionResult> Schemes(CancellationToken ct) => Reply(await service.SchemesAsync(ct));

    [HttpPost("schemes")]
    public async Task<IActionResult> CreateScheme([FromBody] ResearchGrantSchemeCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CreateSchemeAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ResearchGrantCreateDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CreateAsync(input, ct)) : ValidationProblem(ModelState);

    [HttpGet("{reference:guid}")]
    public async Task<IActionResult> Detail(Guid reference, CancellationToken ct) => Reply(await service.DetailAsync(reference, ct));

    [HttpGet("{reference:guid}/milestones")]
    public async Task<IActionResult> Milestones(Guid reference, CancellationToken ct) => Reply(await service.MilestonesAsync(reference, ct));

    [HttpGet("{reference:guid}/history")]
    public async Task<IActionResult> History(Guid reference, int page = 1, int pageSize = 20,
        CancellationToken ct = default) => Reply(await service.HistoryAsync(reference, page, pageSize, ct));

    [HttpPost("{reference:guid}/{action}")]
    public async Task<IActionResult> Command(Guid reference, string action,
        [FromBody] ResearchGrantCommandDto input, CancellationToken ct) =>
        ModelState.IsValid ? Reply(await service.CommandAsync(reference, action, input, ct)) : ValidationProblem(ModelState);
}
