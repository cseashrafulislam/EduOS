using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;

namespace EduOS.Core.Interfaces.IServices;

public interface ITenantUserAdministrationService
{
    Task<ApiResponse<PagedResult<TenantUserListItemDto>>> GetMembersAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<TenantRoleDto>>> GetRolesAsync(CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<TenantPermissionDto>>> GetPermissionsAsync(CancellationToken ct = default);
    Task<ApiResponse<TenantRoleDto>> CreateRoleAsync(TenantRoleCreateRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateRoleAsync(long roleId, TenantRoleUpdateRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetMemberRolesAsync(long membershipId, TenantMemberRolesRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetMemberStatusAsync(long membershipId, TenantMemberStatusRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetRolePermissionsAsync(long roleId, TenantRolePermissionsRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TenantInvitationDto>>> GetInvitationsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<bool>> RevokeInvitationAsync(Guid reference, RevokeTenantInvitationRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> SendInvitationAsync(CreateTenantInvitationRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> AcceptInvitationAsync(AcceptTenantInvitationRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<TenantMemberPermissionOverrideDto>>> GetMemberPermissionOverridesAsync(long membershipId, CancellationToken ct = default);
    Task<ApiResponse<TenantMemberPermissionOverrideDto>> SetMemberPermissionOverrideAsync(long membershipId, long permissionId, SetTenantMemberPermissionOverrideRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<UserCampusAccessDto>>> GetMemberCampusesAsync(long membershipId, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetMemberCampusesAsync(long membershipId, SetMembershipCampusAccessRequestDto request, CancellationToken ct = default);
}
