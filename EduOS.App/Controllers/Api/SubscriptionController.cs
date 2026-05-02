using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers.Api
{
    /// <summary>
    /// Tenant subscription management.
    /// Requires authenticated user with tenant context.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/subscription")]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ISubscriptionInvoiceService _invoiceService;

        public SubscriptionController(
            ISubscriptionService subscriptionService,
            ISubscriptionInvoiceService invoiceService)
        {
            _subscriptionService = subscriptionService;
            _invoiceService = invoiceService;
        }

        /// <summary>
        /// Subscribe to a plan (creates subscription + invoice)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequestDto dto)
        {
            if (dto == null || dto.SubscriptionPlanId <= 0)
                return BadRequest(new { success = false, message = "Plan is required" });

            var result = await _subscriptionService.CreateAsync(dto);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Get current active subscription for the tenant
        /// </summary>
        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent()
        {
            var result = await _subscriptionService.GetCurrentAsync();
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Full subscription history
        /// </summary>
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var result = await _subscriptionService.GetHistoryAsync();
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Cancel subscription
        /// </summary>
        [HttpPost("{id:long}/cancel")]
        public async Task<IActionResult> Cancel(long id, [FromBody] CancelSubscriptionDto dto)
        {
            var result = await _subscriptionService.CancelAsync(
                id, dto?.Reason, dto?.CancelAtPeriodEnd ?? true);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Toggle auto-renew
        /// </summary>
        [HttpPost("{id:long}/auto-renew")]
        public async Task<IActionResult> ToggleAutoRenew(long id, [FromBody] ToggleAutoRenewDto dto)
        {
            var result = await _subscriptionService.ToggleAutoRenewAsync(id, dto.AutoRenew);
            return StatusCode(result.StatusCode, result);
        }

        // ==================== Invoices ====================

        [HttpGet("invoices")]
        public async Task<IActionResult> GetInvoices()
        {
            var result = await _invoiceService.GetMyInvoicesAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("invoices/unpaid")]
        public async Task<IActionResult> GetUnpaidInvoices()
        {
            var result = await _invoiceService.GetUnpaidAsync();
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("invoices/{id:long}")]
        public async Task<IActionResult> GetInvoice(long id)
        {
            var result = await _invoiceService.GetByIdAsync(id);
            return StatusCode(result.StatusCode, result);
        }
    }

    public class CancelSubscriptionDto
    {
        public string? Reason { get; set; }
        public bool CancelAtPeriodEnd { get; set; } = true;
    }

    public class ToggleAutoRenewDto
    {
        public bool AutoRenew { get; set; }
    }
}
