using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Private, scanned, tenant-wide library resources linked to existing Book and Document records.</summary>
public interface ILibraryDigitalResourceService
{
    Task<ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>> GetCampusOptionsAsync(CancellationToken ct = default);
    Task<ApiResponse<PagedResult<LibraryDigitalResourceDto>>> ListAsync(
        Guid? bookReference, string? search, int page, int pageSize, bool includeInactive,
        CancellationToken ct = default);
    Task<ApiResponse<PagedResult<LibraryDigitalAuditDto>>> GetAuditTrailAsync(
        DateOnly? from, DateOnly? to, string? action, int page, int pageSize,
        CancellationToken ct = default);
    Task<ApiResponse<LibraryDigitalResourceDto>> UploadAsync(
        LibraryDigitalUploadRequestDto request, PrivateFileUploadDto file, CancellationToken ct = default);
    Task<ApiResponse<LibraryDigitalResourceDto>> ReplaceAsync(
        Guid reference, LibraryDigitalReplaceRequestDto request, PrivateFileUploadDto file,
        CancellationToken ct = default);
    Task<ApiResponse<LibraryDigitalResourceDto>> UpdateMetadataAsync(
        Guid reference, LibraryDigitalMetadataRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<LibraryDigitalResourceDto>> RevokeAsync(
        Guid reference, LibraryDigitalRevokeRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid reference, CancellationToken ct = default);
}
