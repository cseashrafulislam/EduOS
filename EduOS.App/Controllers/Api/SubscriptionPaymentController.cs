using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api
{
    [ApiController]
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
        [Authorize]
        [HttpPost("initiate")]
        public async Task<IActionResult> Initiate([FromBody] InitiatePaymentRequestDto dto)
        {
            if (dto == null || dto.InvoiceId <= 0)
                return BadRequest(new { success = false, message = "Invoice is required" });

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var result = await _paymentService.InitiateAamarPayAsync(dto, baseUrl);
            return StatusCode(result.StatusCode, result);
        }

        // ============================================================
        // AAMARPAY CALLBACKS (no auth - gateway calls these)
        // ============================================================

        /// <summary>
        /// AamarPay POSTs success callback here
        /// </summary>
        [AllowAnonymous]
        [HttpPost("callback/success")]
        public async Task<IActionResult> SuccessCallback([FromForm] AamarPayCallbackDto dto)
        {
            await _paymentService.HandleAamarPayCallbackAsync(dto);
            // Redirect to success page in app
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            return Redirect($"{baseUrl}/Account/PaymentSuccess?txn={dto.MerTxnid}");
        }

        [AllowAnonymous]
        [HttpPost("callback/fail")]
        public async Task<IActionResult> FailCallback([FromForm] AamarPayCallbackDto dto)
        {
            dto.PayStatus = "Failed";
            await _paymentService.HandleAamarPayCallbackAsync(dto);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            return Redirect($"{baseUrl}/Account/PaymentFailed?txn={dto.MerTxnid}");
        }

        [AllowAnonymous]
        [HttpPost("callback/cancel")]
        public async Task<IActionResult> CancelCallback([FromForm] AamarPayCallbackDto dto)
        {
            dto.PayStatus = "Cancelled";
            await _paymentService.HandleAamarPayCallbackAsync(dto);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            return Redirect($"{baseUrl}/Account/PaymentCancelled?txn={dto.MerTxnid}");
        }

        /// <summary>
        /// AamarPay IPN (server-to-server). Returns plain 200.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("callback/ipn")]
        public async Task<IActionResult> IpnCallback([FromForm] AamarPayCallbackDto dto)
        {
            await _paymentService.HandleAamarPayCallbackAsync(dto);
            return Ok();
        }

        // ============================================================
        // MANUAL PAYMENT (BANK TRANSFER)
        // ============================================================

        [Authorize]
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

        // ============================================================
        // PAYMENT HISTORY
        // ============================================================

        [Authorize]
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
    }

    /// <summary>
    /// Form-binding DTO for multipart upload
    /// </summary>
    public class ManualPaymentSubmitFormDto
    {
        public long InvoiceId { get; set; }
        public string PayerBankName { get; set; } = string.Empty;
        public string PayerAccountNumber { get; set; } = string.Empty;
        public string DepositSlipNumber { get; set; } = string.Empty;
        public DateTime DepositDate { get; set; }
        public decimal Amount { get; set; }
        public string? Note { get; set; }
        public IFormFile? DepositSlip { get; set; }
    }
}
