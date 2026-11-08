using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

public interface ITeacherResourceService
{
    Task<ApiResponse<TeacherStudioOptionsDto>> OptionsAsync(CancellationToken ct = default);
    Task<ApiResponse<PagedResult<TeacherResourceDto>>> ListAsync(string? search, string? state,
        int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<TeacherResourceDto>> UploadAsync(TeacherResourceUploadRequestDto request,
        PrivateFileUploadDto file, CancellationToken ct = default);
    Task<ApiResponse<TeacherResourceDto>> SubmitAsync(Guid reference,
        TeacherResourceTransitionRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<TeacherResourceDto>> ReviewAsync(Guid reference,
        TeacherResourceTransitionRequestDto request, bool approve, CancellationToken ct = default);
    Task<ApiResponse<TeacherResourceDto>> WithdrawAsync(Guid reference,
        TeacherResourceTransitionRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid reference,
        CancellationToken ct = default);
}
