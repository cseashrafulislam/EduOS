using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using Microsoft.AspNetCore.Http;

namespace EduOS.Core.Interfaces.IServices
{
    public interface ISubscriptionInvoiceService
    {
        Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetMyInvoicesAsync();
        Task<ApiResponse<SubscriptionInvoiceDto>> GetByIdAsync(long invoiceId);
        Task<ApiResponse<List<SubscriptionInvoiceDto>>> GetUnpaidAsync();
    }

    public interface ISubscriptionPaymentService
    {
        /// <summary>
        /// Initiate online payment via AamarPay - returns redirect URL
        /// </summary>
        Task<ApiResponse<InitiatePaymentResponseDto>> InitiateAamarPayAsync(InitiatePaymentRequestDto dto);

        /// <summary>
        /// Handle AamarPay IPN callback
        /// </summary>
        Task<ApiResponse<bool>> HandleAamarPayCallbackAsync(AamarPayCallbackDto callback);

        /// <summary>
        /// Submit manual bank transfer details (with deposit slip upload)
        /// </summary>
        Task<ApiResponse<SubscriptionPaymentDto>> SubmitManualPaymentAsync(
            ManualPaymentSubmitDto dto,
            IFormFile? depositSlip);

        Task<ApiResponse<ManualPaymentInstructionsDto>> GetManualPaymentInstructionsAsync(
            long invoiceId);

        Task<ApiResponse<PrivateFileDownloadDto>> GetDepositSlipAsync(long paymentId);

        /// <summary>
        /// SuperAdmin verifies/rejects a manual payment
        /// </summary>
        Task<ApiResponse<bool>> VerifyManualPaymentAsync(VerifyManualPaymentDto dto);

        /// <summary>
        /// Payment history for an invoice
        /// </summary>
        Task<ApiResponse<List<SubscriptionPaymentDto>>> GetByInvoiceAsync(long invoiceId);

        /// <summary>
        /// SuperAdmin: list all manual payments awaiting verification
        /// </summary>
        Task<ApiResponse<List<SubscriptionPaymentDto>>> GetPendingManualVerificationsAsync();
    }
}
