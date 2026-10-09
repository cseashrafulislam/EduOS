using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

public interface ILibraryService
{
    Task<ApiResponse<IReadOnlyList<BookDto>>> GetCatalogAsync(string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookDto>> SaveBookAsync(SaveBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ArchiveBookAsync(Guid reference, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> IssueAsync(IssueBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> CloseAsync(Guid issueReference, ReturnBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<BookIssueDto>>> GetMyIssuesAsync(CancellationToken cancellationToken = default);
}