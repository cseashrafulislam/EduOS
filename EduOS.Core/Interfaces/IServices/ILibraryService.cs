using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Library catalog and issue/return workflow. Server validates copy availability and tenant/student scope atomically.</summary>
public interface ILibraryService
{
    Task<ApiResponse<PagedResult<BookDto>>> GetCatalogAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookDto>> SaveBookAsync(SaveBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ArchiveBookAsync(Guid reference, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> IssueAsync(IssueBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> CloseAsync(Guid issueReference, ReturnBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookIssueDto>>> GetMyIssuesAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
