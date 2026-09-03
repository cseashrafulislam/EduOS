using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices
{
    public interface ISubscriptionService
    {
        /// <summary>
        /// Create a new subscription for the current tenant.
        /// Generates an invoice. For trial plans, immediately activates.
        /// For paid plans, returns invoice details + payment instructions.
        /// </summary>
        Task<ApiResponse<CreateSubscriptionResponseDto>> CreateAsync(CreateSubscriptionRequestDto dto);

        /// <summary>
        /// Get the tenant's current active subscription
        /// </summary>
        Task<ApiResponse<CurrentSubscriptionDto>> GetCurrentAsync();

        /// <summary>
        /// Get full subscription history for the tenant
        /// </summary>
        Task<ApiResponse<List<SubscriptionHistoryDto>>> GetHistoryAsync();

        /// <summary>
        /// Cancel subscription (will remain active until period end if cancelAtPeriodEnd=true)
        /// </summary>
        Task<ApiResponse<bool>> CancelAsync(long subscriptionId, string? reason, bool cancelAtPeriodEnd = true);

        /// <summary>
        /// Toggle auto-renew flag
        /// </summary>
        Task<ApiResponse<bool>> ToggleAutoRenewAsync(long subscriptionId, bool autoRenew);

        /// <summary>
        /// Activate subscription after successful payment.
        /// Called internally by PaymentService.
        /// </summary>
        Task<ApiResponse<bool>> ActivateAfterPaymentAsync(long subscriptionId, long tenantId);

        /// <summary>
        /// Check if tenant's subscription has expired and update status if needed.
        /// Called by Hangfire daily job.
        /// </summary>
        Task<ApiResponse<bool>> CheckExpiryAsync(long tenantId);
    }
}
