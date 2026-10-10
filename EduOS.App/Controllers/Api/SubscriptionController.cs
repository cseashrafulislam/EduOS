using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EduOS.App.Controllers.Api;

[Authorize(Roles = "TenantAdmin")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("api/subscription")]
public sealed class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    private readonly ISubscriptionInvoiceService _invoices;

    public SubscriptionController(ISubscriptionService subscriptions, ISubscriptionInvoiceService invoices)
    {
        _subscriptions = subscriptions;
        _invoices = invoices;
    }

    [HttpPost]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Create([FromBody] StartSubscriptionRequestDto request, CancellationToken ct)
    {
        var response = await _subscriptions.StartAsync(request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var response = await _subscriptions.GetCurrentAsync(ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var response = await _subscriptions.GetHistoryAsync(page, pageSize, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("{subscriptionReference:guid}/cancel")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> Cancel(Guid subscriptionReference,
        [FromBody] CancelSubscriptionRequestDto request, CancellationToken ct)
    {
        var response = await _subscriptions.CancelAsync(subscriptionReference, request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("{subscriptionReference:guid}/auto-renew")]
    [EnableRateLimiting("ApiPolicy")]
    public async Task<IActionResult> SetAutoRenew(Guid subscriptionReference,
        [FromBody] UpdateSubscriptionAutoRenewRequestDto request, CancellationToken ct)
    {
        var response = await _subscriptions.SetAutoRenewAsync(subscriptionReference, request, ct);
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices()
    {
        var response = await _invoices.GetMyInvoicesAsync();
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("invoices/unpaid")]
    public async Task<IActionResult> GetUnpaidInvoices()
    {
        var response = await _invoices.GetUnpaidAsync();
        return StatusCode(response.StatusCode, response);
    }

    [HttpGet("invoices/{id:long}")]
    public async Task<IActionResult> GetInvoice(long id)
    {
        var response = await _invoices.GetByIdAsync(id);
        return StatusCode(response.StatusCode, response);
    }
}
