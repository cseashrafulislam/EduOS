using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers;

public sealed class WebhookTestWorkspaceVm
{
    public PagedResult<WebhookEndpointDto> Endpoints { get; set; } = new();
    public PagedResult<WebhookDeliveryDto> Deliveries { get; set; } = new();
    public long? EndpointId { get; set; }
    public int EndpointPage { get; set; } = 1;
    public int DeliveryPage { get; set; } = 1;
    public string? Error { get; set; }
}

[Authorize(Roles = "TenantAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WebhookTestsController(IIntegrationAdministrationService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int endpointPage = 1, long? endpointId = null,
        int deliveryPage = 1, CancellationToken ct = default)
    {
        endpointPage = Math.Max(1, endpointPage);
        deliveryPage = Math.Max(1, deliveryPage);
        var endpoints = await service.GetWebhookEndpointsAsync(endpointPage, 50, ct);
        if (!endpoints.Success || endpoints.Data == null) return StatusCode(endpoints.StatusCode, endpoints.Message);
        var model = new WebhookTestWorkspaceVm
        {
            Endpoints = endpoints.Data, EndpointPage = endpointPage, DeliveryPage = deliveryPage,
            EndpointId = endpointId ?? endpoints.Data.Items.FirstOrDefault()?.Id
        };
        if (model.EndpointId is > 0)
        {
            var deliveries = await service.GetWebhookDeliveriesAsync(model.EndpointId.Value, deliveryPage, 20, ct);
            if (deliveries.Success && deliveries.Data != null) model.Deliveries = deliveries.Data;
            else model.Error = deliveries.Message;
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(long endpointId, Guid clientRequestId, CancellationToken ct)
    {
        var response = await service.TestWebhookAsync(endpointId,
            new CreateWebhookTestRequestDto { ClientRequestId = clientRequestId }, ct);
        TempData["WebhookNotice"] = response.Message;
        TempData["WebhookNoticeSuccess"] = response.Success;
        return RedirectToAction(nameof(Index), new { endpointId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(long endpointId, long deliveryId, Guid clientRequestId,
        int expectedAttemptCount, CancellationToken ct)
    {
        var response = await service.RetryWebhookAsync(deliveryId,
            new RetryWebhookDeliveryRequestDto
            {
                ClientRequestId = clientRequestId, ExpectedAttemptCount = expectedAttemptCount
            }, ct);
        TempData["WebhookNotice"] = response.Message;
        TempData["WebhookNoticeSuccess"] = response.Success;
        return RedirectToAction(nameof(Index), new { endpointId });
    }
}
