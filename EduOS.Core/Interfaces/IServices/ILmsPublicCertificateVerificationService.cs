using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

public interface ILmsPublicCertificateVerificationService
{
    Task<ApiResponse<LmsPublicVerificationStatusDto>> GetStatusAsync(Guid reference, CancellationToken ct = default);
    Task<ApiResponse<LmsPublicVerificationLinkDto>> EnableAsync(Guid reference,
        LmsPublicVerificationChangeDto request, CancellationToken ct = default);
    Task<ApiResponse<LmsPublicVerificationStatusDto>> DisableAsync(Guid reference,
        LmsPublicVerificationChangeDto request, CancellationToken ct = default);
    Task<ApiResponse<LmsPublicVerificationResultDto>> VerifyAsync(string? token, CancellationToken ct = default);
}
