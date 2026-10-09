using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Versioned, immutable legal acceptance. Consent is bound to the server-authorized user and tenant.</summary>
public interface ILegalAcceptanceService
{
    Task<ApiResponse<IReadOnlyList<LegalDocumentDto>>> GetRequiredDocumentsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<UserLegalAcceptanceDto>> AcceptAsync(AcceptLegalDocumentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<UserLegalAcceptanceDto>>> GetMyAcceptancesAsync(CancellationToken cancellationToken = default);
}
