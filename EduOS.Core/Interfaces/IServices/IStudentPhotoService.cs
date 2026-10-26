using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Student;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentPhotoService
{
    Task<ApiResponse<StudentPhotoDto>> UploadAsync(Guid studentReference, Guid requestId,
        string? rowVersion, PrivateFileUploadDto upload, CancellationToken ct = default);
    Task<ApiResponse<StudentPhotoDto>> GetPortalAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<StudentPhotoDto>> UploadPortalAsync(Guid studentReference, Guid requestId,
        string? rowVersion, PrivateFileUploadDto upload, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadPortalAsync(Guid studentReference,
        Guid assetReference, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid studentReference,
        Guid assetReference, CancellationToken ct = default);
}
