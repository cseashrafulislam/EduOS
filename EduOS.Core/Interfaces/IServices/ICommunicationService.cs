using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;

namespace EduOS.Core.Interfaces.IServices;

public interface ICommunicationService
{
    Task<ApiResponse<PagedResult<NoticeDto>>> GetNoticesAsync(int page, int pageSize, bool? published, CancellationToken cancellationToken = default);
    Task<ApiResponse<NoticeDto>> SaveNoticeAsync(Guid? noticeReference, SaveNoticeRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> MarkNoticeReadAsync(Guid noticeReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<NotificationDto>>> GetMyNotificationsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> MarkNotificationReadAsync(long notificationId, CancellationToken cancellationToken = default);
    Task<ApiResponse<MessageDto>> SendMessageAsync(Guid clientRequestId, SendMessageRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<MessageThreadDto>> GetThreadAsync(Guid threadReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<NotificationPreferenceDto>>> GetMyPreferencesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<NotificationPreferenceDto>> SaveMyPreferenceAsync(SaveNotificationPreferenceRequestDto request, CancellationToken cancellationToken = default);
}
