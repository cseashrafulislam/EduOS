using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentGuardianWorkspaceService
{
    Task<ApiResponse<StudentGuardianWorkspaceDto>> GetAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<StudentGuardianWorkspaceDto>> CreateAndLinkAsync(CreateStudentGuardianWorkspaceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<StudentGuardianWorkspaceDto>> UpdateLinkAsync(Guid studentReference, long linkId, UpdateStudentGuardianWorkspaceLinkDto request, CancellationToken ct = default);
}
