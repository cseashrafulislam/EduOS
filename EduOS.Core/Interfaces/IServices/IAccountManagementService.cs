using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Current authenticated user's profile, password, and session revocation.</summary>
public interface IAccountManagementService
{
    Task<ApiResponse<UserSummaryDto>> GetMyProfileAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<UserSummaryDto>> UpdateMyProfileAsync(UpdateUserProfileRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ChangePasswordAsync(ChangePasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<ActiveSessionDto>>> GetMySessionsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> RevokeMySessionAsync(RevokeSessionRequestDto request, CancellationToken cancellationToken = default);
}
