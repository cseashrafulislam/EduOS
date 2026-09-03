using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api
{
    [ApiController]
    [AutoValidateAntiforgeryToken]
    [Route("api/subscription-payment")]
    public class SubscriptionPaymentController : ControllerBase
    {
        private readonly ISubscriptionPaymentService _paymentService;

        public SubscriptionPaymentController(ISubscriptionPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        // ============================================================
        // INITIATE ONLINE PAYMENT (AamarPay)
        // ============================================================
        [Authorize(Roles = "TenantAdmin")]
        [HttpPost("initiate")]
        public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequestDto dto)
        {
            if (dto == null || dto.InvoiceId <= 0)
                return BadRequest(new { success = false, message = "Invoice is required" });

            var result = await _paymentService.InitiateAamarPayAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // AAMARPAY CALLBACKS (no auth - gateway calls these)
        // ============================================================

        /// <summary>
        /// AamarPay POSTs success callback here
        /// </summary>
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("PaymentCallbackPolicy")]
        [HttpPost("callback/success")]
        public async Task<IActionResult> SuccessCallback([FromForm] AamarPayCallbackDto dto)
        {
            var result = await _paymentService.HandleAamarPayCallbackAsync(dto);
            // Redirect to success page in app
            var outcome = result.Success ? "PaymentSuccess" : "PaymentFailed";
            return LocalRedirect($"/Account/{outcome}?txn={Uri.EscapeDataString(dto.MerTxnid ?? string.Empty)}");
        }

        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("PaymentCallbackPolicy")]
        [HttpPost("callback/fail")]
        public async Task<IActionResult> FailCallback([FromForm] AamarPayCallbackDto dto)
        {
            dto.PayStatus = "Failed";
            await _paymentService.HandleAamarPayCallbackAsync(dto);
            return LocalRedirect($"/Account/PaymentFailed?txn={Uri.EscapeDataString(dto.MerTxnid ?? string.Empty)}");
        }

        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("PaymentCallbackPolicy")]
        [HttpPost("callback/cancel")]
        public async Task<IActionResult> CancelCallback([FromForm] AamarPayCallbackDto dto)
        {
            dto.PayStatus = "Cancelled";
            await _paymentService.HandleAamarPayCallbackAsync(dto);
            return LocalRedirect($"/Account/PaymentCancelled?txn={Uri.EscapeDataString(dto.MerTxnid ?? string.Empty)}");
        }

        /// <summary>
        /// AamarPay IPN (server-to-server). Returns plain 200.
        /// </summary>
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("PaymentCallbackPolicy")]
        [HttpPost("callback/ipn")]
        public async Task<IActionResult> IpnCallback([FromForm] AamarPayCallbackDto dto)
        {
            var result = await _paymentService.HandleAamarPayCallbackAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // MANUAL PAYMENT (BANK TRANSFER)
        // ============================================================

        [Authorize(Roles = "TenantAdmin")]
        [HttpPost("manual")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SubmitManualPayment(
            [FromForm] ManualPaymentSubmitFormDto form)
        {
            var dto = new ManualPaymentSubmitDto
            {
                InvoiceId = form.InvoiceId,
                PayerBankName = form.PayerBankName,
                PayerAccountNumber = form.PayerAccountNumber,
                DepositSlipNumber = form.DepositSlipNumber,
                DepositDate = form.DepositDate,
                Amount = form.Amount,
                Note = form.Note
            };

            var result = await _paymentService.SubmitManualPaymentAsync(dto, form.DepositSlip);
            return StatusCode(result.StatusCode, result);
        }

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet("manual-instructions/{invoiceId:long}")]
        public async Task<IActionResult> GetManualPaymentInstructions(long invoiceId)
        {
            var result = await _paymentService.GetManualPaymentInstructionsAsync(invoiceId);
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // PAYMENT HISTORY
        // ============================================================

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet("invoice/{invoiceId:long}")]
        public async Task<IActionResult> GetByInvoice(long invoiceId)
        {
            var result = await _paymentService.GetByInvoiceAsync(invoiceId);
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // SUPERADMIN: VERIFY MANUAL PAYMENTS
        // ============================================================

        [Authorize(Roles = "SuperAdmin")]
        [HttpGet("admin/pending-verifications")]
        public async Task<IActionResult> GetPendingVerifications()
        {
            var result = await _paymentService.GetPendingManualVerificationsAsync();
            return StatusCode(result.StatusCode, result);
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost("admin/verify")]
        public async Task<IActionResult> VerifyManualPayment([FromBody] VerifyManualPaymentDto dto)
        {
            var result = await _paymentService.VerifyManualPaymentAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpGet("admin/payments/{paymentId:long}/deposit-slip")]
        public async Task<IActionResult> DownloadDepositSlip(long paymentId)
        {
            var result = await _paymentService.GetDepositSlipAsync(paymentId);
            if (!result.Success || result.Data == null)
                return StatusCode(result.StatusCode, result);

            Response.Headers.CacheControl = "no-store, private";
            return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
        }
    }

    /// <summary>
    /// Form-binding DTO for multipart upload
    /// </summary>
    public class ManualPaymentSubmitFormDto
    {
        [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)]
        public long InvoiceId { get; set; }
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(150)]
        public string PayerBankName { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(100)]
        public string PayerAccountNumber { get; set; } = string.Empty;
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(100)]
        public string DepositSlipNumber { get; set; } = string.Empty;
        public DateTime DepositDate { get; set; }
        [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0.01", "999999999999.99")]
        public decimal Amount { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(500)]
        public string? Note { get; set; }
        public IFormFile? DepositSlip { get; set; }
    }
}
