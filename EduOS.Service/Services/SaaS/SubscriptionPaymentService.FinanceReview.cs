using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

public sealed partial class SubscriptionPaymentService
{
    private static readonly string[] FinanceTriageDecisions =
        ["Investigating", "ProviderDisputeOpened", "FinanceEscalated", "AwaitingRefundInstructions", "ExternalCaseClosed"];

    private static string? AuditString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool AuditBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long? AuditLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number) ? number : null;

    private static GatewayFinanceHistoryItemDto MapFinanceAudit(AuditLog row)
    {
        var dto = new GatewayFinanceHistoryItemDto
        {
            AuditId = row.Id, PaymentId = row.EntityId ?? 0, OccurredAtUtc = row.OccurredAt,
            ReviewerUserId = row.UserId, Action = row.Action,
            ProviderConfirmed = row.IsSuccess && row.Action != "GatewayFinanceReview"
        };
        if (string.IsNullOrWhiteSpace(row.NewValue)) return dto;
        try
        {
            using var doc = JsonDocument.Parse(row.NewValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return dto;
            var data = doc.RootElement;
            dto.Decision = AuditString(data, "Decision");
            dto.Note = row.Action == "GatewayFinanceReview" ? AuditString(data, "Note") : null;
            dto.EvidenceAuditId = AuditLong(data, "EvidenceAuditId");
            dto.EvidenceSha256 = AuditString(data, "EvidenceSha256");
            dto.CaseClosed = AuditBool(data, "CaseClosed") && row.Action == "GatewayFinanceReview";
            dto.ExternalCaseReference = dto.CaseClosed ? AuditString(data, "ExternalCaseReference") : null;
            dto.RequiresManualReview = AuditBool(data, "RequiresManualReview") ||
                row.Action == "GatewayFinanceReview";
        }
        catch (JsonException) { /* Legacy audit payloads are not assumed to be JSON. */ }
        return dto;
    }

    /// <summary>Tenant-bound, paginated history of provider inspection and finance triage.</summary>
    public async Task<ApiResponse<PagedResult<GatewayFinanceHistoryItemDto>>> GetGatewayFinanceHistoryAsync(
        long paymentId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!_user.IsSuperAdmin)
            return ApiResponse<PagedResult<GatewayFinanceHistoryItemDto>>.ErrorResponse("Platform administrator access required.", 403);
        if (paymentId <= 0 || page is < 1 or > 100000 || pageSize is < 1 or > 50 ||
            page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<GatewayFinanceHistoryItemDto>>.ErrorResponse("Invalid payment or paging.", 400);
        if (_gatewayEvidenceAudit == null)
            return ApiResponse<PagedResult<GatewayFinanceHistoryItemDto>>.ErrorResponse("Investigation history is unavailable.", 503);
        var payment = await _payments.GetByIdForPlatformAsync(paymentId, ct);
        if (payment == null || payment.PaymentMethod != PaymentMethodType.Gateway ||
            payment.GatewayCode != "AamarPay")
            return ApiResponse<PagedResult<GatewayFinanceHistoryItemDto>>.ErrorResponse("Gateway payment not found.", 404);
        var invoice = await _invoices.GetByIdForSystemAsync(payment.SubscriptionInvoiceId, payment.TenantId, ct);
        if (invoice == null || !await _subscriptionRows.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(s => s.Id == invoice.TenantSubscriptionId && s.TenantId == payment.TenantId && !s.IsDeleted, ct))
            return ApiResponse<PagedResult<GatewayFinanceHistoryItemDto>>.ErrorResponse("Tenant-scoped invoice not found.", 409);
        var query = _gatewayEvidenceAudit.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == payment.TenantId && a.EntityName == "SubscriptionPayment" &&
                a.EntityId == payment.Id && !a.IsDeleted &&
                (a.Action == "GatewayProviderInspection" || a.Action == "GatewayProviderEscalation" ||
                 a.Action == "GatewayFinanceReview"));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<GatewayFinanceHistoryItemDto>>.SuccessResponse(new()
        {
            Items = rows.Select(MapFinanceAudit).ToList(), TotalCount = count,
            Page = page, PageSize = pageSize
        });
    }

    /// <summary>
    /// Append a finance review linked to independently verified provider evidence.
    /// Never changes payment, invoice, subscription, refund or ledger state.
    /// Serializable transaction plus request ID makes retries idempotent.
    /// </summary>
    public async Task<ApiResponse<GatewayFinanceHistoryItemDto>> RecordGatewayFinanceReviewAsync(
        long paymentId, GatewayFinanceReviewRequestDto request, CancellationToken ct = default)
    {
        if (!_user.IsSuperAdmin)
            return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Platform administrator access required.", 403);
        if (paymentId <= 0 || request == null || request.RequestId == Guid.Empty ||
            request.EvidenceAuditId <= 0 || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 200 || !FinanceTriageDecisions.Contains(request.Decision) ||
            string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length is < 10 or > 500)
            return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Invalid finance review.", 400);
        if (_gatewayEvidenceAudit == null)
            return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Finance review audit is unavailable.", 503);
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Invalid row version.", 400); }
        if (expected.Length is < 1 or > 32)
            return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Invalid row version.", 400);

        var note = request.Note.Trim();
        var closing = request.Decision == "ExternalCaseClosed";
        var externalCaseReference = request.ExternalCaseReference?.Trim();
        if ((closing && (string.IsNullOrWhiteSpace(externalCaseReference) ||
                externalCaseReference.Length is < 6 or > 100 ||
                !externalCaseReference.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '/'))) ||
            (!closing && !string.IsNullOrWhiteSpace(externalCaseReference)))
            return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("A valid external case reference is required only for case closure.", 400);
        var idempotencyKey = request.RequestId.ToString("N");
        // Preserve the existing fingerprint for non-closing reviews and retries.
        var fingerprintInput = string.Join("\n", paymentId, request.EvidenceAuditId, request.Decision, note, request.RowVersion);
        if (closing) fingerprintInput += "\n" + externalCaseReference;
        var requestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)));
        try
        {
            using var tx = SerializableScope();
            var payment = await _payments.GetByIdForPlatformAsync(paymentId, ct);
            if (payment == null || payment.PaymentMethod != PaymentMethodType.Gateway ||
                payment.GatewayCode != "AamarPay")
                return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Gateway payment not found.", 404);
            var invoice = await _invoices.GetByIdForSystemAsync(payment.SubscriptionInvoiceId, payment.TenantId, ct);
            if (invoice == null || !await _subscriptionRows.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(s => s.Id == invoice.TenantSubscriptionId && s.TenantId == payment.TenantId && !s.IsDeleted, ct))
                return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Tenant-scoped invoice not found.", 409);
            var audits = _gatewayEvidenceAudit.GetQueryable().IgnoreQueryFilters();
            var previous = await audits.AsNoTracking().FirstOrDefaultAsync(a =>
                a.TenantId == payment.TenantId && a.EntityName == "SubscriptionPayment" &&
                a.EntityId == payment.Id && !a.IsDeleted && a.Action == "GatewayFinanceReview" &&
                a.RequestId == idempotencyKey, ct);
            if (previous != null)
            {
                if (previous.OldValue != requestFingerprint)
                    return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Request ID already used for a different review.", 409);
                tx.Complete();
                return ApiResponse<GatewayFinanceHistoryItemDto>.SuccessResponse(MapFinanceAudit(previous));
            }
            // A case can be closed once, after at least one recorded investigation.
            // Audit is the existing durable owner of finance follow-up; no payment state is changed.
            var latestReview = await audits.AsNoTracking()
                .Where(a => a.TenantId == payment.TenantId && a.EntityName == "SubscriptionPayment" &&
                    a.EntityId == payment.Id && !a.IsDeleted && a.Action == "GatewayFinanceReview")
                .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
                .Select(a => a.NewValue).FirstOrDefaultAsync(ct);
            if (closing && latestReview == null)
                return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Record an investigation before closing the external case.", 409);
            if (!string.IsNullOrWhiteSpace(latestReview))
            {
                try
                {
                    using var last = JsonDocument.Parse(latestReview);
                    if (last.RootElement.ValueKind == JsonValueKind.Object &&
                        AuditBool(last.RootElement, "CaseClosed"))
                        return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("External finance case is already closed.", 409);
                }
                catch (JsonException) { /* Old audit payloads cannot authorize closure. */ }
            }
            if (payment.State != PaymentState.Initiated ||
                payment.InitiatedAt > DateTime.UtcNow.Subtract(GatewayRecoveryDelay))
                return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Payment is no longer under investigation.", 409);
            if (payment.RowVersion.Length != expected.Length ||
                !CryptographicOperations.FixedTimeEquals(payment.RowVersion, expected))
                return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Payment changed. Reload and retry.", 409);

            var evidence = await audits.AsNoTracking().FirstOrDefaultAsync(a =>
                a.Id == request.EvidenceAuditId && a.TenantId == payment.TenantId &&
                a.EntityName == "SubscriptionPayment" && a.EntityId == payment.Id &&
                a.Action == "GatewayProviderEscalation" && a.IsSuccess && !a.IsDeleted, ct);
            if (evidence == null || evidence.OccurredAt < DateTime.UtcNow.AddHours(-24))
                return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Fresh provider-confirmed escalation evidence is required.", 409);
            string? fingerprint;
            try
            {
                using var doc = JsonDocument.Parse(evidence.NewValue ?? "");
                var data = doc.RootElement;
                if (data.ValueKind != JsonValueKind.Object)
                    return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Escalation evidence is invalid.", 409);
                fingerprint = AuditString(data, "EvidenceSha256");
                if (!AuditBool(data, "RequiresManualReview") ||
                    AuditString(data, "Status") != "Successful" ||
                    fingerprint == null || fingerprint.Length != 64 ||
                    !fingerprint.All(Uri.IsHexDigit))
                    return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Escalation evidence is invalid.", 409);
            }
            catch (JsonException)
            {
                return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Escalation evidence is invalid.", 409);
            }
            var row = new AuditLog
            {
                TenantId = payment.TenantId, UserId = _user.UserId, EntityName = "SubscriptionPayment",
                EntityId = payment.Id, Action = "GatewayFinanceReview",
                RequestId = idempotencyKey, OldValue = requestFingerprint, IsSuccess = true,
                OccurredAt = DateTime.UtcNow,
                NewValue = JsonSerializer.Serialize(new
                {
                    request.Decision, Note = note, request.EvidenceAuditId,
                    EvidenceSha256 = fingerprint, RequiresManualReview = true,
                    CaseClosed = closing, ExternalCaseReference = closing ? externalCaseReference : null
                })
            };
            await _gatewayEvidenceAudit.AddAsync(row);
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            _logger.LogInformation("Gateway finance review {AuditId} recorded for payment {PaymentId}, tenant {TenantId}",
                row.Id, payment.Id, payment.TenantId);
            return ApiResponse<GatewayFinanceHistoryItemDto>.SuccessResponse(MapFinanceAudit(row));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent gateway finance review for payment {PaymentId}", paymentId);
            return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Concurrent review conflict. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway finance review failed for payment {PaymentId}", paymentId);
            return ApiResponse<GatewayFinanceHistoryItemDto>.ErrorResponse("Finance review could not be recorded.", 503);
        }
    }
}
