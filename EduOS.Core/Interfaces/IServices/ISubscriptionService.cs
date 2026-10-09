using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-owned subscription lifecycle. Invoice payment and payment verification have separate canonical owners.</summary>
public interface ISubscriptionService
{
    Task<ApiResponse<TenantSubscriptionDto>> StartAsync(StartSubscriptionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantSubscriptionDto?>> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<TenantSubscriptionDto>>> GetHistoryAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantSubscriptionDto>> CancelAsync(Guid subscriptionReference, CancelSubscriptionRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantSubscriptionDto>> SetAutoRenewAsync(Guid subscriptionReference, UpdateSubscriptionAutoRenewRequestDto request, CancellationToken cancellationToken = default);
}
