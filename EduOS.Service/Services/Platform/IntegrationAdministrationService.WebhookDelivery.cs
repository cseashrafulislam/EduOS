using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Platform;

public sealed partial class IntegrationAdministrationService
{
    private const string WebhookTestScope = "integration.webhook.test";
    private const int MaxWebhookAttempts = 3;

    public async Task<ApiResponse<WebhookDeliveryDto>> TestWebhookAsync(
        long endpointId, CreateWebhookTestRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Tenant administrator required.", 403);
        if (endpointId <= 0 || request == null || request.ClientRequestId == Guid.Empty)
            return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Valid webhook and request ID are required.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(endpointId.ToString(CultureInfo.InvariantCulture))));
        try
        {
            var result = await unit.ExecuteInTransactionAsync(async token =>
            {
                var existing = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(
                    x => x.TenantId == user.TenantId && x.Scope == WebhookTestScope &&
                         x.RequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.RequestHash != fingerprint ||
                        !long.TryParse(existing.ResultHash, NumberStyles.None, CultureInfo.InvariantCulture, out var existingId))
                        return (Response: ApiResponse<WebhookDeliveryDto>.ErrorResponse("Request ID already used.", 409), Created: false, Id: 0L);
                    var previous = await db.WebhookDeliveries.AsNoTracking().FirstOrDefaultAsync(
                        x => x.TenantId == user.TenantId && x.Id == existingId && !x.IsDeleted, token);
                    return previous == null
                        ? (Response: ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery no longer available.", 409), Created: false, Id: 0L)
                        : (Response: ApiResponse<WebhookDeliveryDto>.SuccessResponse(Map(previous)), Created: false, Id: previous.Id);
                }
                var endpoint = await db.WebhookEndpoints.AsNoTracking().FirstOrDefaultAsync(
                    x => x.TenantId == user.TenantId && x.Id == endpointId && !x.IsDeleted, token);
                if (endpoint == null) return (Response: ApiResponse<WebhookDeliveryDto>.ErrorResponse("Webhook not found.", 404), Created: false, Id: 0L);
                if (!endpoint.IsActive || string.IsNullOrWhiteSpace(endpoint.ProtectedSecret) || !IsPublicWebhookUrl(endpoint.Url))
                    return (Response: ApiResponse<WebhookDeliveryDto>.ErrorResponse("Active webhook with valid destination and secret required.", 409), Created: false, Id: 0L);
                var now = DateTime.UtcNow;
                var eventId = Guid.NewGuid();
                var delivery = new WebhookDelivery
                {
                    TenantId = user.TenantId, WebhookEndpointId = endpointId, EventId = eventId,
                    EventCode = "WEBHOOK.TEST", State = WebhookDeliveryState.Pending,
                    PayloadJson = JsonSerializer.Serialize(new { eventId, eventCode = "WEBHOOK.TEST", occurredAt = now, test = true }),
                    CreatedBy = user.UserId
                };
                db.WebhookDeliveries.Add(delivery);
                await db.SaveChangesAsync(token);
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    TenantId = user.TenantId, Scope = WebhookTestScope, RequestId = request.ClientRequestId,
                    RequestHash = fingerprint, ResultHash = delivery.Id.ToString(CultureInfo.InvariantCulture),
                    ExpiresAt = now.AddDays(30), CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);
                return (Response: ApiResponse<WebhookDeliveryDto>.SuccessResponse(Map(delivery)), Created: true, Id: delivery.Id);
            }, IsolationLevel.Serializable, cancellationToken);
            if (!result.Created) return result.Response;
            return await SendWebhookAttemptAsync(result.Id, null, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Webhook changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Webhook request already processed.", 409); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook test creation failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Webhook test could not be started.", 500);
        }
    }

    public async Task<ApiResponse<WebhookDeliveryDto>> RetryWebhookAsync(
        long deliveryId, RetryWebhookDeliveryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Tenant administrator required.", 403);
        if (deliveryId <= 0 || request == null || request.ClientRequestId == Guid.Empty || request.ExpectedAttemptCount < 1)
            return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery and version are required.");
        return await SendWebhookAttemptAsync(deliveryId, request, cancellationToken);
    }

    private async Task<ApiResponse<WebhookDeliveryDto>> SendWebhookAttemptAsync(
        long deliveryId, RetryWebhookDeliveryRequestDto? retryRequest, CancellationToken cancellationToken)
    {
        WebhookDelivery delivery;
        WebhookEndpoint endpoint;
        int claimedAttempt;
        try
        {
            var claim = await unit.ExecuteInTransactionAsync(async token =>
            {
                var row = await db.WebhookDeliveries.FirstOrDefaultAsync(
                    x => x.Id == deliveryId && x.TenantId == user.TenantId && !x.IsDeleted, token);
                if (row == null) return (Code: 404, Message: "Delivery not found.", Delivery: (WebhookDelivery?)null, Endpoint: (WebhookEndpoint?)null, Attempt: 0);
                if (row.EventCode != "WEBHOOK.TEST")
                    return (Code: 409, Message: "Only administrator test deliveries can be retried here.", Delivery: (WebhookDelivery?)null, Endpoint: (WebhookEndpoint?)null, Attempt: 0);
                if (retryRequest != null)
                {
                    var duplicate = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(
                        x => x.TenantId == user.TenantId && x.Scope == "integration.webhook.retry" &&
                             x.RequestId == retryRequest.ClientRequestId && !x.IsDeleted, token);
                    if (duplicate != null)
                        return duplicate.ResultHash == deliveryId.ToString(CultureInfo.InvariantCulture)
                            ? (Code: 208, Message: "Retry request already processed.", Delivery: row, Endpoint: (WebhookEndpoint?)null, Attempt: 0)
                            : (Code: 409, Message: "Retry request ID already used.", Delivery: (WebhookDelivery?)null, Endpoint: (WebhookEndpoint?)null, Attempt: 0);
                }
                if (retryRequest != null && row.AttemptCount != retryRequest.ExpectedAttemptCount)
                    return (Code: 409, Message: "Delivery changed; reload before retrying.", Delivery: (WebhookDelivery?)null, Endpoint: (WebhookEndpoint?)null, Attempt: 0);
                var now = DateTime.UtcNow;
                var retryable = retryRequest == null
                    ? row.State == WebhookDeliveryState.Pending && row.AttemptCount == 0
                    : row.State == WebhookDeliveryState.Failed ||
                      (row.State == WebhookDeliveryState.Pending && row.NextAttemptAt <= now);
                if (!retryable || row.AttemptCount >= MaxWebhookAttempts)
                    return (Code: 409, Message: "Delivery is not eligible for retry.", Delivery: (WebhookDelivery?)null, Endpoint: (WebhookEndpoint?)null, Attempt: 0);
                var hook = await db.WebhookEndpoints.AsNoTracking().FirstOrDefaultAsync(
                    x => x.Id == row.WebhookEndpointId && x.TenantId == user.TenantId && !x.IsDeleted, token);
                if (hook == null || !hook.IsActive || string.IsNullOrWhiteSpace(hook.ProtectedSecret) ||
                    !IsPublicWebhookUrl(hook.Url))
                    return (Code: 409, Message: "Webhook destination is unavailable or inactive.", Delivery: (WebhookDelivery?)null, Endpoint: (WebhookEndpoint?)null, Attempt: 0);
                row.State = WebhookDeliveryState.Pending;
                row.AttemptCount++;
                row.LastAttemptAt = now;
                row.NextAttemptAt = now.AddMinutes(2); // crash-recovery lease
                row.FailureReason = null;
                row.UpdatedAt = now;
                row.UpdatedBy = user.UserId;
                if (retryRequest != null)
                    db.IdempotencyRecords.Add(new IdempotencyRecord
                    {
                        TenantId = user.TenantId, Scope = "integration.webhook.retry",
                        RequestId = retryRequest.ClientRequestId,
                        RequestHash = deliveryId.ToString(CultureInfo.InvariantCulture),
                        ResultHash = deliveryId.ToString(CultureInfo.InvariantCulture),
                        ExpiresAt = now.AddDays(30), CreatedBy = user.UserId
                    });
                await db.SaveChangesAsync(token);
                return (Code: 200, Message: "", Delivery: row, Endpoint: hook, Attempt: row.AttemptCount);
            }, IsolationLevel.Serializable, cancellationToken);
            if (claim.Code == 208 && claim.Delivery != null)
                return ApiResponse<WebhookDeliveryDto>.SuccessResponse(Map(claim.Delivery), claim.Message);
            if (claim.Code != 200 || claim.Delivery == null || claim.Endpoint == null)
                return ApiResponse<WebhookDeliveryDto>.ErrorResponse(claim.Message, claim.Code);
            delivery = claim.Delivery;
            endpoint = claim.Endpoint;
            claimedAttempt = claim.Attempt;
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery could not be claimed.", 409); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook delivery claim failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery claim failed.", 500);
        }

        (bool Succeeded, string? Failure) outcome;
        try
        {
            var secret = protection.CreateProtector("EduOS.Integrations.WebhookSecrets.v1").Unprotect(endpoint.ProtectedSecret);
            outcome = await SecureWebhookTransport.SendAsync(
                endpoint.Url, delivery.PayloadJson, secret, delivery.EventId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook transport unavailable for tenant {TenantId}", user.TenantId);
            outcome = (false, "Webhook secret or transport unavailable.");
        }
        try
        {
            var current = await db.WebhookDeliveries.FirstOrDefaultAsync(
                x => x.Id == deliveryId && x.TenantId == user.TenantId && !x.IsDeleted, cancellationToken);
            if (current == null) return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery not found.", 404);
            if (current.AttemptCount != claimedAttempt || current.State != WebhookDeliveryState.Pending)
                return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery changed during transport; reload.", 409);
            current.State = outcome.Succeeded ? WebhookDeliveryState.Succeeded :
                claimedAttempt >= MaxWebhookAttempts ? WebhookDeliveryState.Dead : WebhookDeliveryState.Failed;
            current.FailureReason = outcome.Succeeded ? null : (outcome.Failure ?? "Delivery failed.");
            current.NextAttemptAt = outcome.Succeeded || claimedAttempt >= MaxWebhookAttempts
                ? null : DateTime.UtcNow.AddMinutes(2 * claimedAttempt);
            current.UpdatedAt = DateTime.UtcNow;
            current.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(cancellationToken);
            return ApiResponse<WebhookDeliveryDto>.SuccessResponse(Map(current),
                outcome.Succeeded ? "Webhook delivered." : "Webhook attempt failed; review delivery history.");
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery changed; reload.", 409); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook result persistence failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<WebhookDeliveryDto>.ErrorResponse("Delivery outcome could not be persisted; retry after lease expiry.", 500);
        }
    }
}
