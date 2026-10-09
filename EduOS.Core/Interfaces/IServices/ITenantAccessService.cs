using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Authorization boundary for tenant membership, roles, campus access and invitations.</summary>
public interface ITenantAccessService
{
    Task<ApiResponse<PagedResult<TenantMembershipDto>>> GetMembersAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantMembershipDto>> AddMemberAsync(CreateTenantMembershipRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantMembershipDto>> ChangeMemberStatusAsync(long membershipId, UpdateTenantMembershipStatusRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantMembershipDto>> SetMemberRolesAsync(long membershipId, SetMembershipRolesRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<UserCampusAccessDto>>> GetMemberCampusesAsync(long membershipId, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<UserCampusAccessDto>>> SetMemberCampusesAsync(long membershipId, SetMembershipCampusAccessRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantInvitationDto>> InviteAsync(CreateTenantInvitationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantMembershipDto>> AcceptInvitationAsync(AcceptTenantInvitationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> RevokeInvitationAsync(Guid invitationReference, CancellationToken cancellationToken = default);
}
