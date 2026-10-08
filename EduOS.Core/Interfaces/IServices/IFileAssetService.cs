using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface IFileAssetService
{
    Task<ApiResponse<FileAssetDto>> RegisterVerifiedUploadAsync(Guid clientRequestId, CreateFileAssetMetadataRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<FileAssetDto>> GetMetadataAsync(Guid fileReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<FileAssetDto>> RecordSecurityScanAsync(Guid fileReference, RecordFileSecurityScanRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> AuthorizeDownloadAsync(Guid fileReference, CancellationToken cancellationToken = default);
}
