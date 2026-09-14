using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

public interface ILibraryService
{
    Task<ApiResponse<IReadOnlyList<LibraryBookDto>>> GetCatalogAsync(string? search, CancellationToken cancellationToken = default);
    Task<ApiResponse<LibraryIssueDto>> IssueAsync(IssueBookDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LibraryIssueDto>> CloseAsync(Guid issueReference, ReturnBookDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<LibraryIssueDto>>> GetMyIssuesAsync(CancellationToken cancellationToken = default);
}
