using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Library catalog and issue/return workflow. Server validates copy availability and tenant/student scope atomically.</summary>
public interface ILibraryService
{
    Task<ApiResponse<IReadOnlyList<BookCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<BookCategoryDto>> SaveCategoryAsync(long? categoryId, SaveBookCategoryRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookDto>>> GetCatalogAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LibraryBranchDto>>> GetAssignableBranchesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookCopyDto>>> GetBookCopiesAsync(Guid bookReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookCopyDto>>> GetAvailableCopiesForStudentAsync(Guid bookReference, Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookCopyDto>>> GetAvailableCopiesForEmployeeAsync(Guid bookReference, Guid employeeReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LibraryEmployeeOptionDto>>> FindEligibleEmployeesAsync(string search, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookCopyDto>> SaveBookCopyAsync(long? copyId, SaveBookCopyRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookDto>> SaveBookAsync(SaveBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ArchiveBookAsync(Guid reference, string rowVersion, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> IssueAsync(IssueBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> IssueEmployeeAsync(IssueBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> RenewAsync(Guid issueReference, RenewBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookRenewalHistoryDto>>> GetRenewalHistoryAsync(Guid issueReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookIssueDto>> CloseAsync(Guid issueReference, ReturnBookRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookIssueDto>>> GetMyIssuesAsync(int page, int pageSize, CancellationToken cancellationToken = default, string? status = null);
    Task<ApiResponse<PagedResult<BookIssueDto>>> GetMyEmployeeIssuesAsync(int page, int pageSize, string? status, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LibraryCopyConditionDto>>> GetCopyConditionsAsync(
        string? accession, string? state, long? campusId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<LibraryCopyConditionDto>> ChangeCopyConditionAsync(
        long copyId, ChangeLibraryCopyConditionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LibraryCopyConditionHistoryDto>>> GetCopyConditionHistoryAsync(
        long copyId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LibraryCopyIntegrityDto>>> GetCopyIntegrityAsync(
        string? accession, long? campusId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LibraryOverdueBranchDto>>> GetOverdueBranchesAsync(
        string? search, long? campusId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<LibraryCirculationRowDto>>> GetCirculationRegisterAsync(
        string? search, string? status, long? campusId, int page, int pageSize, CancellationToken cancellationToken = default);
}
