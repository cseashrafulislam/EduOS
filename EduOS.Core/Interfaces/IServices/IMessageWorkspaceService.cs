using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;

namespace EduOS.Core.Interfaces.IServices;

public interface IMessageWorkspaceService
{
    Task<ApiResponse<PagedResult<NoticeReadReceiptDto>>> GetNoticeReadReceiptsAsync(Guid noticeReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<DeviceTokenDto>>> GetMyDevicesAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<MessageThreadSummaryDto>>> GetMyThreadsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<MessageRecipientDto>>> SearchRecipientsAsync(string query, CancellationToken cancellationToken = default);
    Task<ApiResponse<MessageThreadDto>> GetThreadForMessageAsync(long messageId, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> MarkThreadReadAsync(Guid threadReference, CancellationToken cancellationToken = default);
}
