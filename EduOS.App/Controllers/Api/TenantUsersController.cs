using EduOS.Core.DTOs.Auth;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[Route("api/tenant-users")]
public sealed class TenantUsersController(ITenantUserAdministrationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Members([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var r = await service.GetMembersAsync(page, pageSize, search, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken ct)
    {
        var r = await service.GetRolesAsync(ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> Permissions(CancellationToken ct)
    {
        var r = await service.GetPermissionsAsync(ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] TenantRoleCreateRequestDto request, CancellationToken ct)
    {
        var r = await service.CreateRoleAsync(request, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPut("{membershipId:long}/roles")]
    public async Task<IActionResult> SetRoles(long membershipId,
        [FromBody] TenantMemberRolesRequestDto request, CancellationToken ct)
    {
        var r = await service.SetMemberRolesAsync(membershipId, request, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPut("{membershipId:long}/status")]
    public async Task<IActionResult> SetStatus(long membershipId,
        [FromBody] TenantMemberStatusRequestDto request, CancellationToken ct)
    {
        var r = await service.SetMemberStatusAsync(membershipId, request, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPut("roles/{roleId:long}/permissions")]
    public async Task<IActionResult> SetPermissions(long roleId,
        [FromBody] TenantRolePermissionsRequestDto request, CancellationToken ct)
    {
        var r = await service.SetRolePermissionsAsync(roleId, request, ct);
        return StatusCode(r.StatusCode, r);
    }
}
