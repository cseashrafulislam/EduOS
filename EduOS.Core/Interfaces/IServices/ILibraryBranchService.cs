using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
namespace EduOS.Core.Interfaces.IServices;
public interface ILibraryBranchService
{
    Task<ApiResponse<IReadOnlyList<LibraryBranchDto>>> GetBranchesAsync(long? campusId, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>> CampusesAsync(CancellationToken ct = default);
    Task<ApiResponse<LibraryBranchDto>> SaveBranchAsync(long? id, SaveLibraryBranchRequestDto request, CancellationToken ct = default);
}