using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Campus-authorized, bounded in-app overdue notices; never posts a financial charge.</summary>
public interface ILibraryOverdueNoticeService
{
    Task<ApiResponse<LibraryOverdueNoticePreviewDto>> PreviewAsync(
        long campusId, CancellationToken cancellationToken = default);
    Task<ApiResponse<LibraryOverdueNoticeBatchDto>> SendBatchAsync(
        SendLibraryOverdueNoticesRequestDto request, CancellationToken cancellationToken = default);
}
