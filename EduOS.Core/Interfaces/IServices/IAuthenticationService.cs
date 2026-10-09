using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Authentication and token lifecycle. Refresh-token rotation is atomic and replay-resistant.</summary>
public interface IAuthenticationService
{
    Task<ApiResponse<LoginResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LoginResponseDto>> RefreshAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> LogoutAsync(LogoutRequestDto request, CancellationToken cancellationToken = default);
    /// <summary>Always return the same public response, whether the email exists or not.</summary>
    Task<ApiResponse<bool>> RequestPasswordResetAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ResendVerificationAsync(ResendVerificationRequestDto request, CancellationToken cancellationToken = default);
}
