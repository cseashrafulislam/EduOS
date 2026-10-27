using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface IWorkspacePreferencesService
{
    Task<ApiResponse<WorkspacePreferencesDto>> GetAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<WorkspacePreferencesDto>> SaveAsync(SaveWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken = default);
    Task<ApiResponse<WorkspacePreferencesDto>> ResetAsync(ResetWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken = default);
}
