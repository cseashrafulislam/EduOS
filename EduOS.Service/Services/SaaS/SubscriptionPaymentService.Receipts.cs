using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.SaaS;

public sealed partial class SubscriptionPaymentService
{
    /// <summary>
    /// Returns a receipt only for a settled payment owned by the current institution.
    /// It never changes payment or invoice state and does not expose payer bank details.
    /// </summary>
    public async Task<ApiResponse<SubscriptionPaymentReceiptDto>> GetReceiptAsync(
        long paymentId, CancellationToken cancellationToken = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return ApiResponse<SubscriptionPaymentReceiptDto>.ErrorResponse("Institution administrator access required.", 403);
        if (paymentId <= 0)
            return ApiResponse<SubscriptionPaymentReceiptDto>.ErrorResponse("Invalid payment.", 400);

        var tenantId = _user.TenantId;
        var settled = await (
            from payment in _payments.GetQueryable().AsNoTracking()
            join invoice in _invoices.GetQueryable().AsNoTracking()
                on new { InvoiceId = payment.SubscriptionInvoiceId, payment.TenantId }
                equals new { InvoiceId = invoice.Id, invoice.TenantId }
            where payment.Id == paymentId && payment.TenantId == tenantId &&
                  !payment.IsDeleted && !invoice.IsDeleted &&
                  payment.State == PaymentState.Successful && payment.CompletedAt != null
            select new
            {
                payment.PublicId, payment.Id, payment.TransactionId,
                payment.PaymentMethod, payment.Amount, payment.CurrencyCode,
                payment.CompletedAt, payment.ProviderTransactionId, invoice.InvoiceNumber
            }).SingleOrDefaultAsync(cancellationToken);

        // Do not reveal whether a foreign, pending, failed or refunded payment exists.
        if (settled == null)
            return ApiResponse<SubscriptionPaymentReceiptDto>.ErrorResponse("Confirmed payment not found.", 404);

        var institutionName = await _tenants.GetQueryable().AsNoTracking()
            .Where(x => x.Id == tenantId && !x.IsDeleted)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(institutionName))
            return ApiResponse<SubscriptionPaymentReceiptDto>.ErrorResponse("Institution not found.", 404);

        return ApiResponse<SubscriptionPaymentReceiptDto>.SuccessResponse(new SubscriptionPaymentReceiptDto
        {
            PaymentId = settled.Id,
            ReceiptNumber = "RCPT-" + settled.PublicId.ToString("N").ToUpperInvariant(),
            InstitutionName = institutionName,
            InvoiceNumber = settled.InvoiceNumber,
            TransactionId = settled.TransactionId,
            PaymentMethod = settled.PaymentMethod.ToString(),
            Amount = settled.Amount,
            CurrencyCode = settled.CurrencyCode,
            CompletedAtUtc = DateTime.SpecifyKind(settled.CompletedAt!.Value, DateTimeKind.Utc),
            ProviderTransactionId = settled.ProviderTransactionId
        });
    }
}
