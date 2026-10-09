using EduOS.Core.Common;
using EduOS.Core.DTOs.Communication;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Protected tenant administration of communication gateway credentials, templates, and notice categories.</summary>
public interface ICommunicationAdministrationService
{
    Task<ApiResponse<IReadOnlyList<CommunicationGatewayDto>>> GetGatewaysAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<CommunicationGatewayDto>> SaveGatewayAsync(long? gatewayId, SaveCommunicationGatewayRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<MessageTemplateDto>>> GetTemplatesAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<MessageTemplateDto>> SaveTemplateAsync(long? templateId, SaveMessageTemplateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<NoticeCategoryDto>>> GetNoticeCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<NoticeCategoryDto>> SaveNoticeCategoryAsync(long? categoryId, SaveNoticeCategoryRequestDto request, CancellationToken cancellationToken = default);
}
