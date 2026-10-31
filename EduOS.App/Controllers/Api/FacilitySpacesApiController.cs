using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Facilities;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController, Route("api/facilities/spaces"), Authorize, RequireModule("FACILITIES"),
 AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FacilitySpacesApiController : ControllerBase
{
    private readonly IFacilitySpaceService service;
    public FacilitySpacesApiController(IFacilitySpaceService service) => this.service = service;
    [HttpGet, Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager,FacilitiesTechnician,FacilitiesApprover")]
    public async Task<IActionResult> List(long? campusId = null, string? search = null, int page = 1,
        int pageSize = 25, CancellationToken ct = default) =>
        Reply(await service.ListAsync(campusId, search, page, pageSize, ct));
    [HttpPost, Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Create([FromBody] SaveFacilitySpaceRequestDto request, CancellationToken ct) =>
        Reply(await service.SaveAsync(null, request, ct));
    [HttpPut("{id:long}"), Authorize(Roles = "TenantAdmin,Principal,FacilitiesManager")]
    public async Task<IActionResult> Update(long id, [FromBody] SaveFacilitySpaceRequestDto request, CancellationToken ct) =>
        Reply(await service.SaveAsync(id, request, ct));
    private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
}
