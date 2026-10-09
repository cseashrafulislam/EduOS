using EduOS.Core.Common;
using EduOS.Core.DTOs.System;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Restricted tenant-administrator operations; credential plaintext may be returned only at creation.</summary>
public interface IIntegrationAdministrationService
{
    Task<ApiResponse<PagedResult<WebhookEndpointDto>>> GetWebhookEndpointsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<WebhookEndpointDto>> SaveWebhookEndpointAsync(long? endpointId, SaveWebhookEndpointRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<WebhookDeliveryDto>>> GetWebhookDeliveriesAsync(long endpointId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<ApiCredentialDto>>> GetApiCredentialsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<ApiCredentialCreatedDto>> CreateApiCredentialAsync(CreateApiCredentialRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> RevokeApiCredentialAsync(long credentialId, RevokeApiCredentialRequestDto request, CancellationToken cancellationToken = default);
}
