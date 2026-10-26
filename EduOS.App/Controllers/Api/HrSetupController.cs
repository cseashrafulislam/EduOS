using EduOS.App.Authorization;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Route("api/hr/setup")]
[Authorize(Roles = "TenantAdmin,Principal,HR")]
[RequireModule("HR")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HrSetupController : ControllerBase
{
    private readonly IHrSetupService service;
    public HrSetupController(IHrSetupService implementation) => service = implementation;
    private IActionResult Reply<T>(EduOS.Core.Common.ApiResponse<T> response) => StatusCode(response.StatusCode, response);
    [HttpGet("departments")]
    public async Task<IActionResult> Departments([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Reply(await service.GetOrganizationUnitsAsync(page, pageSize, ct));
    [HttpPost("departments"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CreateDepartment([FromBody] SaveOrganizationUnitRequestDto request, CancellationToken ct)
        => Reply(await service.SaveOrganizationUnitAsync(null, request, ct));
    [HttpPut("departments/{id:long}"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateDepartment(long id, [FromBody] SaveOrganizationUnitRequestDto request, CancellationToken ct)
        => Reply(await service.SaveOrganizationUnitAsync(id, request, ct));
    [HttpGet("designations")]
    public async Task<IActionResult> Designations(CancellationToken ct) => Reply(await service.GetDesignationsAsync(ct));
    [HttpPost("designations"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CreateDesignation([FromBody] SaveDesignationRequestDto request, CancellationToken ct)
        => Reply(await service.SaveDesignationAsync(null, request, ct));
    [HttpPut("designations/{id:long}"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateDesignation(long id, [FromBody] SaveDesignationRequestDto request, CancellationToken ct)
        => Reply(await service.SaveDesignationAsync(id, request, ct));
    [HttpGet("shifts")]
    public async Task<IActionResult> Shifts(CancellationToken ct) => Reply(await service.GetWorkShiftsAsync(ct));
    [HttpPost("shifts"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> CreateShift([FromBody] SaveWorkShiftRequestDto request, CancellationToken ct)
        => Reply(await service.SaveWorkShiftAsync(null, request, ct));
    [HttpPut("shifts/{id:long}"), Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> UpdateShift(long id, [FromBody] SaveWorkShiftRequestDto request, CancellationToken ct)
        => Reply(await service.SaveWorkShiftAsync(id, request, ct));
}
