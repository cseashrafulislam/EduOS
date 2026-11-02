using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/facilities/visitors"), Authorize, RequireModule("FACILITIES"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacilityVisitorsApiController(IFacilityVisitorService service) : ControllerBase
{
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);

    [HttpGet]
    public async Task<IActionResult> List(long? campusId, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.ListAsync(campusId, page, pageSize, ct));

    [HttpGet("hosts"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Hosts(long campusId, string? search = null, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.HostsAsync(campusId, search, page, pageSize, ct));

    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Create([FromBody] CreateFacilityVisitorDto request,
        CancellationToken ct) => Reply(await service.CreateAsync(request, ct));

    [HttpPost("{id:long}/{action}")]
    public async Task<IActionResult> Transition(long id, string action,
        [FromBody] FacilityVisitorTransitionDto request, CancellationToken ct) =>
        Reply(await service.TransitionAsync(id, action, request, ct));

    [HttpGet("{id:long}/history")]
    public async Task<IActionResult> History(long id, int page = 1, int pageSize = 25,
        CancellationToken ct = default) => Reply(await service.HistoryAsync(id, page, pageSize, ct));
}
