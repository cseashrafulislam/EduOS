using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Research;

namespace EduOS.Core.Interfaces.IServices;

public interface IResearchEvidenceFileService
{
    Task<ApiResponse<PagedResult<ResearchEvidenceFileDto>>> ListAsync(string purpose, Guid parentReference,
        int page = 1, int pageSize = 25, CancellationToken ct = default);
    Task<ApiResponse<ResearchEvidenceFileDto>> UploadAsync(string purpose, Guid parentReference,
        Guid clientRequestId, PrivateFileUploadDto upload, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(string purpose, Guid parentReference,
        Guid fileReference, CancellationToken ct = default);
}
