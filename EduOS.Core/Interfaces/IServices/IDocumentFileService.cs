using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface IDocumentFileService
{
    Task<ApiResponse<FileAssetDto>> UploadAsync(Guid clientRequestId, PrivateFileUploadDto upload, CancellationToken cancellationToken = default);
    Task<ApiResponse<FileAssetDto>> UploadPortalAsync(Guid studentReference, Guid clientRequestId, PrivateFileUploadDto upload, CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid fileReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<FileAssetDto>>> GetUnattachedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
