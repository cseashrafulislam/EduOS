using EduOS.Core.DTOs.Auth;
using System.Text;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[ApiController]
[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/tenant-users")]
public sealed class TenantUsersController(ITenantUserAdministrationService service) : ControllerBase
{
    [HttpGet("invitations")]
    public async Task<IActionResult> Invitations([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var r = await service.GetInvitationsAsync(page, pageSize, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPost("invitations/{reference:guid}/revoke")]
    public async Task<IActionResult> RevokeInvitation(Guid reference, [FromBody] RevokeTenantInvitationRequestDto request, CancellationToken ct)
    {
        var r = await service.RevokeInvitationAsync(reference, request, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPost("invitations")]
    public async Task<IActionResult> SendInvitation([FromBody] CreateTenantInvitationRequestDto request, CancellationToken ct)
    {
        var r = await service.SendInvitationAsync(request, ct);
        return StatusCode(r.StatusCode, r);
    }

    [AllowAnonymous]
    [EnableRateLimiting("AccountSecurityPolicy")]
    [HttpPost("invitations/accept")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptTenantInvitationRequestDto request, CancellationToken ct)
    {
        var r = await service.AcceptInvitationAsync(request, ct);
        return StatusCode(r.StatusCode, r);
    }

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

    [HttpPut("roles/{roleId:long}")]
    public async Task<IActionResult> UpdateRole(long roleId,
        [FromBody] TenantRoleUpdateRequestDto request, CancellationToken ct)
    {
        var r = await service.UpdateRoleAsync(roleId, request, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpGet("{membershipId:long}/permission-overrides")]
    public async Task<IActionResult> MemberPermissionOverrides(long membershipId, CancellationToken ct)
    {
        var r = await service.GetMemberPermissionOverridesAsync(membershipId, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPut("{membershipId:long}/permission-overrides/{permissionId:long}")]
    public async Task<IActionResult> SetMemberPermissionOverride(long membershipId, long permissionId,
        [FromBody] SetTenantMemberPermissionOverrideRequestDto request, CancellationToken ct)
    {
        var r = await service.SetMemberPermissionOverrideAsync(membershipId, permissionId, request, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpGet("{membershipId:long}/access-history")]
    public async Task<IActionResult> MemberAccessHistory(long membershipId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var r = await service.GetMemberAccessHistoryAsync(membershipId, page, pageSize, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpGet("{membershipId:long}/access-history/export")]
    public async Task<IActionResult> ExportMemberAccessHistory(long membershipId, CancellationToken ct)
    {
        var result = await service.ExportMemberAccessHistoryCsvAsync(membershipId, ct);
        if (!result.Success) return StatusCode(result.StatusCode, result);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(Encoding.UTF8.GetBytes(result.Data ?? string.Empty),
            "text/csv", "institution-member-access-history.csv");
    }

    [HttpGet("{membershipId:long}/campuses")]
    public async Task<IActionResult> GetCampuses(long membershipId, CancellationToken ct)
    {
        var r = await service.GetMemberCampusesAsync(membershipId, ct);
        return StatusCode(r.StatusCode, r);
    }

    [HttpPut("{membershipId:long}/campuses")]
    public async Task<IActionResult> SetCampuses(long membershipId,
        [FromBody] SetMembershipCampusAccessRequestDto request, CancellationToken ct)
    {
        var r = await service.SetMemberCampusesAsync(membershipId, request, ct);
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
