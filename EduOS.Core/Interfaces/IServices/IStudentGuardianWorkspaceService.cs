using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentGuardianWorkspaceService
{
    Task<ApiResponse<StudentGuardianWorkspaceDto>> GetAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<StudentGuardianWorkspaceDto>> CreateAndLinkAsync(CreateStudentGuardianWorkspaceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<StudentGuardianWorkspaceDto>> LinkExistingAsync(LinkExistingStudentGuardianRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<StudentGuardianWorkspaceDto>> UnlinkAsync(Guid studentReference, long linkId, UnlinkStudentGuardianWorkspaceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<StudentGuardianWorkspaceDto>> ReactivateAsync(Guid studentReference, long linkId, ReactivateStudentGuardianWorkspaceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<StudentGuardianWorkspaceDto>> UpdateLinkAsync(Guid studentReference, long linkId, UpdateStudentGuardianWorkspaceLinkDto request, CancellationToken ct = default);
}
