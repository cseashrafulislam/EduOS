using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
namespace EduOS.Core.Interfaces.IServices;
public interface IDocumentVerificationService
{
    Task<ApiResponse<PagedResult<DocumentVerificationDto>>> GetReviewQueueAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<DocumentVerificationDto>> ReviewAsync(Guid documentReference, DocumentVerificationDecisionDto request, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadForReviewAsync(Guid documentReference, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadApprovedAsync(Guid documentReference, CancellationToken ct = default);
}