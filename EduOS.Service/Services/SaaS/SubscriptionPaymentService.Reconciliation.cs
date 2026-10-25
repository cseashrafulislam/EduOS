using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.SaaS;

public sealed partial class SubscriptionPaymentService
{
    /// <summary>
    /// Platform-only reconciliation history. Reuses the canonical payment/invoice/
    /// subscription owners; never changes settled financial rows or tenant states.
    /// </summary>
    public async Task<ApiResponse<PagedManualPaymentReconciliationDto>> GetManualPaymentReconciliationHistoryAsync(
        int page, int pageSize, string? search, string? outcome, CancellationToken ct = default)
    {
        if (!_user.IsSuperAdmin)
            return ApiResponse<PagedManualPaymentReconciliationDto>.ErrorResponse(
                "Platform administrator access required.", 403);
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || search?.Length > 100 ||
            (outcome != null && outcome != "" &&
             !string.Equals(outcome, "All", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(outcome, "Approved", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(outcome, "Rejected", StringComparison.OrdinalIgnoreCase)))
            return ApiResponse<PagedManualPaymentReconciliationDto>.ErrorResponse(
                "Invalid paging, search or outcome filter.", 400);

        // Cross-tenant access is intentional and restricted to SuperAdmin above.
        // Join on BOTH tenant and aggregate identifiers to prevent cross-tenant
        // invoice/subscription associations even with legacy inconsistent records.
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
            where !payment.IsDeleted && !invoice.IsDeleted &&
                  !subscription.IsDeleted && !tenant.IsDeleted &&
                  payment.PaymentMethod == PaymentMethodType.BankTransfer &&
                  payment.VerifiedAt != null &&
                  (payment.State == PaymentState.Successful || payment.State == PaymentState.Failed)
            select new { payment, invoice, subscription, tenant };

        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x =>
                x.payment.TransactionId.StartsWith(term) ||
                x.invoice.InvoiceNumber.StartsWith(term) ||
                x.tenant.Name.StartsWith(term) ||
                x.tenant.Code.StartsWith(term));
        if (string.Equals(outcome, "Approved", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.payment.State == PaymentState.Successful);
        else if (string.Equals(outcome, "Rejected", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.payment.State == PaymentState.Failed);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.payment.VerifiedAt)
            .ThenByDescending(x => x.payment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ManualPaymentReconciliationDto
            {
                PaymentId = x.payment.Id,
                TenantId = x.payment.TenantId,
                InstitutionName = x.tenant.Name,
                InstitutionCode = x.tenant.Code,
                InvoiceNumber = x.invoice.InvoiceNumber,
                TransactionId = x.payment.TransactionId,
                DepositSlipNumber = x.payment.DepositSlipNumber,
                Amount = x.payment.Amount,
                CurrencyCode = x.payment.CurrencyCode,
                PaymentState = x.payment.State,
                VerifiedAt = x.payment.VerifiedAt,
                VerifiedByUserId = x.payment.VerifiedByUserId,
                VerificationNote = x.payment.VerificationNote,
                InvoiceTotalAmount = x.invoice.TotalAmount,
                InvoicePaidAmount = x.invoice.PaidAmount,
                InvoiceDueAmount = x.invoice.DueAmount,
                InvoiceState = x.invoice.State,
                SubscriptionState = x.subscription.State,
                PeriodStartsAt = x.subscription.StartsAt,
                PeriodEndsAt = x.subscription.EndsAt
            }).ToListAsync(ct);

        return ApiResponse<PagedManualPaymentReconciliationDto>.SuccessResponse(
            new PagedManualPaymentReconciliationDto
            {
                Items = rows, TotalCount = total, Page = page, PageSize = pageSize
            });
    }
}
