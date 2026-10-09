using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Library master data and copy status. Issued copies cannot change state outside the issue/return workflow.</summary>
public interface ILibraryCatalogAdministrationService
{
    Task<ApiResponse<IReadOnlyList<BookCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<BookCategoryDto>> SaveCategoryAsync(long? categoryId, SaveBookCategoryRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<LibraryBranchDto>>> GetBranchesAsync(long? campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<LibraryBranchDto>> SaveBranchAsync(long? branchId, SaveLibraryBranchRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<BookCopyDto>>> GetBookCopiesAsync(Guid bookReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookCopyDto>> SaveBookCopyAsync(long? bookCopyId, SaveBookCopyRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BookCopyDto>> ChangeCopyStateAsync(long copyId, ChangeBookCopyStateRequestDto request, CancellationToken cancellationToken = default);
}
