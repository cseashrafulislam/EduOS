using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.SaaS;

public sealed partial class SubscriptionPaymentService
{
    private static readonly TimeSpan GatewayRecoveryDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Aged, initiated gateway payments with explicit tenant joins. Read-only.
    /// No provider lookup is performed during paging.
    /// </summary>
    public Task<ApiResponse<PagedGatewayPaymentRecoveryDto>> GetPendingGatewayRecoveryAsync(
        int page, int pageSize, string? search, CancellationToken ct = default) =>
        GetGatewayExceptionQueueAsync(page, pageSize, search, "All", ct);

    /// <summary>
    /// Read-only exception triage of aged initiated payments. Neither age nor invoice
    /// status proves provider failure; only verified callbacks may settle funds.
    /// </summary>
    public async Task<ApiResponse<PagedGatewayPaymentRecoveryDto>> GetGatewayExceptionQueueAsync(
        int page, int pageSize, string? search, string? category, CancellationToken ct = default)
    {
        if (!_user.IsSuperAdmin)
            return ApiResponse<PagedGatewayPaymentRecoveryDto>.ErrorResponse("Platform administrator access required.", 403);
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || search?.Length > 100 ||
            category is not (null or "" or "All" or "Blocked" or "Overdue" or "Awaiting"))
            return ApiResponse<PagedGatewayPaymentRecoveryDto>.ErrorResponse("Invalid paging or exception category.", 400);
        var now = DateTime.UtcNow;
        var cutoff = now.Subtract(GatewayRecoveryDelay);
        var overdueCutoff = now.AddHours(-24);
        var query =
            from payment in _payments.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            join invoice in _invoices.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on new { Id = payment.SubscriptionInvoiceId, payment.TenantId }
                equals new { invoice.Id, invoice.TenantId }
            join subscription in _subscriptionRows.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on new { Id = invoice.TenantSubscriptionId, invoice.TenantId }
                equals new { subscription.Id, subscription.TenantId }
            join tenant in _tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on payment.TenantId equals tenant.Id
            where !payment.IsDeleted && !invoice.IsDeleted && !subscription.IsDeleted && !tenant.IsDeleted
                && payment.PaymentMethod == PaymentMethodType.Gateway && payment.GatewayCode == "AamarPay"
                && payment.State == PaymentState.Initiated && payment.InitiatedAt <= cutoff
            select new { payment, invoice, tenant };
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.payment.TransactionId.StartsWith(term) ||
                x.invoice.InvoiceNumber.StartsWith(term) || x.tenant.Name.StartsWith(term) ||
                x.tenant.Code.StartsWith(term));
        if (category == "Blocked")
            query = query.Where(x => x.invoice.State == InvoiceState.Paid ||
                x.invoice.State == InvoiceState.Cancelled || x.invoice.State == InvoiceState.Refunded ||
                x.invoice.DueAmount < x.payment.Amount || x.payment.Amount <= 0 ||
                x.invoice.CurrencyCode.ToUpper() != x.payment.CurrencyCode.ToUpper());
        else if (category == "Overdue")
            query = query.Where(x => x.payment.InitiatedAt <= overdueCutoff);
        else if (category == "Awaiting")
            query = query.Where(x => x.payment.InitiatedAt > overdueCutoff &&
                x.invoice.State != InvoiceState.Paid && x.invoice.State != InvoiceState.Cancelled &&
                x.invoice.State != InvoiceState.Refunded && x.invoice.DueAmount >= x.payment.Amount &&
                x.payment.Amount > 0 && x.invoice.CurrencyCode.ToUpper() == x.payment.CurrencyCode.ToUpper());
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.payment.InitiatedAt).ThenBy(x => x.payment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.payment.Id, x.payment.TenantId, x.payment.TransactionId,
                x.payment.Amount, x.payment.CurrencyCode, x.payment.InitiatedAt,
                x.payment.RowVersion, x.invoice.InvoiceNumber, x.invoice.DueAmount,
                InvoiceState = x.invoice.State, InvoiceCurrency = x.invoice.CurrencyCode,
                InstitutionName = x.tenant.Name, InstitutionCode = x.tenant.Code
            }).ToListAsync(ct);
        return ApiResponse<PagedGatewayPaymentRecoveryDto>.SuccessResponse(
            new PagedGatewayPaymentRecoveryDto
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = rows.Select(x =>
                {
                    var closed = x.InvoiceState is InvoiceState.Paid or InvoiceState.Cancelled or InvoiceState.Refunded;
                    var insufficient = x.Amount <= 0 || x.DueAmount < x.Amount;
                    var currencyMismatch = !string.Equals(x.InvoiceCurrency, x.CurrencyCode, StringComparison.OrdinalIgnoreCase);
                    var blocked = closed || insufficient || currencyMismatch;
                    var hours = (int)Math.Clamp((now - x.InitiatedAt).TotalHours, 0, int.MaxValue);
                    return new GatewayPaymentRecoveryDto
                    {
                        PaymentId = x.Id, TenantId = x.TenantId, TransactionId = x.TransactionId,
                        Amount = x.Amount, CurrencyCode = x.CurrencyCode, InitiatedAt = x.InitiatedAt,
                        InvoiceNumber = x.InvoiceNumber, InvoiceDueAmount = x.DueAmount,
                        InstitutionName = x.InstitutionName, InstitutionCode = x.InstitutionCode,
                        RowVersion = Convert.ToBase64String(x.RowVersion),
                        AgeHours = hours, CanReconcile = !blocked,
                        ExceptionCategory = blocked ? "Blocked" : hours >= 24 ? "Overdue" : "Awaiting",
                        ExceptionReason = closed ? "Invoice is already closed; investigate before any settlement." :
                            insufficient ? "Invoice balance cannot accept this payment." :
                            currencyMismatch ? "Invoice and payment currencies differ." :
                            hours >= 24 ? "Unresolved for 24+ hours; verify the provider outcome." :
                            "Awaiting authoritative gateway verification."
                    };
                }).ToList()
            });
    }

    /// <summary>
    /// Recovery never trusts a browser-supplied status, amount or transaction ID.
    /// It delegates financial settlement to the existing provider-verified callback.
    /// </summary>
    public async Task<ApiResponse<bool>> ReconcileGatewayPaymentAsync(long paymentId, string rowVersion)
    {
        if (!_user.IsSuperAdmin)
            return ApiResponse<bool>.ErrorResponse("Platform administrator access required.", 403);
        if (paymentId <= 0 || string.IsNullOrWhiteSpace(rowVersion))
            return ApiResponse<bool>.ErrorResponse("Payment and current row version are required.", 400);
        byte[] expected;
        try { expected = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { return ApiResponse<bool>.ErrorResponse("Invalid row version.", 400); }
        if (expected.Length == 0 || expected.Length > 32)
            return ApiResponse<bool>.ErrorResponse("Invalid row version.", 400);
        var payment = await _payments.GetByIdForPlatformAsync(paymentId);
        if (payment == null || payment.PaymentMethod != PaymentMethodType.Gateway ||
            payment.GatewayCode != "AamarPay")
            return ApiResponse<bool>.ErrorResponse("Gateway payment not found.", 404);
        if (payment.State != PaymentState.Initiated ||
            payment.InitiatedAt > DateTime.UtcNow.Subtract(GatewayRecoveryDelay))
            return ApiResponse<bool>.ErrorResponse("Payment is not eligible for reconciliation.", 409);
        if (payment.RowVersion.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(payment.RowVersion, expected))
            return ApiResponse<bool>.ErrorResponse("Payment changed. Reload and retry.", 409);
        _logger.LogInformation("SuperAdmin {ReviewerId} requested provider reconciliation for payment {PaymentId} tenant {TenantId}",
            _user.UserId, payment.Id, payment.TenantId);
        return await HandleAamarPayCallbackAsync(new AamarPayCallbackDto { MerTxnid = payment.TransactionId });
    }

    /// <summary>
    /// Independently inspect a provider outcome even when the invoice is blocked.
    /// This is an evidence/audit operation only: it NEVER settles a payment, changes
    /// an invoice, or activates a subscription. The raw gateway response is not
    /// exposed or copied into the audit log; its SHA-256 fingerprint is retained.
    /// </summary>
    public async Task<ApiResponse<GatewayProviderInspectionDto>> InspectGatewayProviderAsync(
        long paymentId, string rowVersion, CancellationToken ct = default)
    {
        if (!_user.IsSuperAdmin)
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Platform administrator access required.", 403);
        if (paymentId <= 0 || string.IsNullOrWhiteSpace(rowVersion))
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Payment and row version are required.", 400);
        if (_gatewayEvidenceAudit == null)
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Gateway investigation audit is unavailable.", 503);
        byte[] expected;
        try { expected = Convert.FromBase64String(rowVersion); }
        catch (FormatException)
        {
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Invalid row version.", 400);
        }
        if (expected.Length is < 1 or > 32)
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Invalid row version.", 400);

        var payment = await _payments.GetByIdForPlatformAsync(paymentId, ct);
        if (payment == null || payment.PaymentMethod != PaymentMethodType.Gateway ||
            payment.GatewayCode != "AamarPay")
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Gateway payment not found.", 404);
        if (payment.State != PaymentState.Initiated ||
            payment.InitiatedAt > DateTime.UtcNow.Subtract(GatewayRecoveryDelay))
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Payment is no longer eligible for investigation.", 409);
        if (payment.RowVersion.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(payment.RowVersion, expected))
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Payment changed. Reload and retry.", 409);

        // The payment, invoice and subscription must belong to the same tenant.
        var invoice = await _invoices.GetByIdForSystemAsync(payment.SubscriptionInvoiceId, payment.TenantId, ct);
        if (invoice == null || !await _subscriptionRows.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.Id == invoice.TenantSubscriptionId &&
                x.TenantId == payment.TenantId && !x.IsDeleted, ct))
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse("Tenant-scoped invoice or subscription not found.", 409);

        var closed = invoice.State is InvoiceState.Paid or InvoiceState.Cancelled or InvoiceState.Refunded;
        var balanceBlocked = payment.Amount <= 0 || invoice.DueAmount < payment.Amount;
        var invoiceCurrencyMatches = string.Equals(invoice.CurrencyCode, payment.CurrencyCode,
            StringComparison.OrdinalIgnoreCase);
        var blocked = closed || balanceBlocked || !invoiceCurrencyMatches;
        try
        {
            // Network request occurs before any audit database write and cannot settle funds.
            var evidence = await _gateway.VerifyTransactionAsync(payment.TransactionId, ct);
            var amountMatches = decimal.TryParse(evidence.Amount, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var providerAmount) &&
                providerAmount == payment.Amount;
            // A missing provider currency is not affirmative evidence for an investigation.
            var currencyMatches = !string.IsNullOrWhiteSpace(evidence.Currency) &&
                string.Equals(evidence.Currency, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
                invoiceCurrencyMatches;
            var confirmed = evidence.IsSuccess;
            var needsReview = confirmed && (blocked || !amountMatches || !currencyMatches);
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(evidence.RawResponse ?? string.Empty)));
            var finding = needsReview
                ? "Provider reports success, but invoice/amount/currency requires manual finance investigation. No funds were applied."
                : confirmed
                    ? "Provider reports success and values match. Use the existing verified reconciliation action to apply payment."
                    : "Provider success was not confirmed. Do not credit the invoice.";
            var observedAt = DateTime.UtcNow;
            var audit = new EduOS.Core.Entities.System.AuditLog
            {
                TenantId = payment.TenantId, UserId = _user.UserId,
                Action = needsReview ? "GatewayProviderEscalation" : "GatewayProviderInspection",
                EntityName = "SubscriptionPayment", EntityId = payment.Id,
                IsSuccess = confirmed, OccurredAt = observedAt,
                NewValue = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Status = confirmed ? "Successful" : "Unconfirmed",
                    AmountMatches = amountMatches, CurrencyMatches = currencyMatches,
                    InvoiceBlocked = blocked, RequiresManualReview = needsReview,
                    EvidenceSha256 = fingerprint
                })
            };
            await _gatewayEvidenceAudit.AddAsync(audit);
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Gateway provider inspection recorded: payment {PaymentId}, tenant {TenantId}, audit {AuditId}, escalation {Escalation}",
                payment.Id, payment.TenantId, audit.Id, needsReview);
            return ApiResponse<GatewayProviderInspectionDto>.SuccessResponse(new GatewayProviderInspectionDto
            {
                PaymentId = payment.Id, TenantId = payment.TenantId,
                InspectedAtUtc = observedAt, ProviderStatus = confirmed ? "Successful" : "Unconfirmed",
                ProviderConfirmed = confirmed, AmountMatches = amountMatches,
                CurrencyMatches = currencyMatches, InvoiceBlocked = blocked,
                RequiresManualReview = needsReview, Finding = finding,
                EvidenceSha256 = fingerprint, AuditId = audit.Id
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway provider inspection failed for payment {PaymentId}", paymentId);
            return ApiResponse<GatewayProviderInspectionDto>.ErrorResponse(
                "Provider inspection or its audit could not be completed. No payment was applied.", 503);
        }
    }
}
