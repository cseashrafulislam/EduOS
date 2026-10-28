using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;

namespace EduOS.Core.Interfaces.IServices;

public interface IPublicCertificateVerificationService
{
    Task<ApiResponse<PublicCertificateVerificationDto>> VerifyAsync(
        Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PublicTranscriptVerificationDto>> VerifyTranscriptAsync(
        Guid reference, CancellationToken cancellationToken = default);
}
