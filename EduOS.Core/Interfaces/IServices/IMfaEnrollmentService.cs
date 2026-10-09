using EduOS.Core.Common;
using EduOS.Core.DTOs.Auth;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Opt-in MFA enrollment, verification and recovery-code lifecycle.</summary>
public interface IMfaEnrollmentService
{
    Task<ApiResponse<TwoFactorStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<MfaEnrollmentStartDto>> BeginEnrollmentAsync(MfaSetupRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<MfaRecoveryCodesDto>> EnableAsync(MfaEnableRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> DisableAsync(ChangePasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LoginResponseDto>> CompleteLoginChallengeAsync(MfaLoginRequestDto request, CancellationToken cancellationToken = default);
}
