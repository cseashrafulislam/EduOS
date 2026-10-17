using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface IMessageAttachmentService
{
    Task<ApiResponse<MessageAttachmentListingDto>> GetForMessageAsync(long messageId, CancellationToken cancellationToken = default);
    Task<ApiResponse<MessageAttachmentDto>> UploadAsync(long messageId, Guid requestId, PrivateFileUploadDto upload, CancellationToken cancellationToken = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(long messageId, Guid reference, CancellationToken cancellationToken = default);
}
