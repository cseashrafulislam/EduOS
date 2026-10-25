using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.SaaS;

/// <summary>Read-only, platform-only history of bank-transfer verification decisions.</summary>
public sealed class ManualPaymentReconciliationDto
{
    public long PaymentId { get; set; }
    public long TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string? DepositSlipNumber { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public PaymentState PaymentState { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public long? VerifiedByUserId { get; set; }
    public string? VerificationNote { get; set; }
    public decimal InvoiceTotalAmount { get; set; }
    public decimal InvoicePaidAmount { get; set; }
    public decimal InvoiceDueAmount { get; set; }
    public InvoiceState InvoiceState { get; set; }
    public SubscriptionState SubscriptionState { get; set; }
    public DateTime PeriodStartsAt { get; set; }
    public DateTime PeriodEndsAt { get; set; }
}

public sealed class PagedManualPaymentReconciliationDto
{
    public IReadOnlyList<ManualPaymentReconciliationDto> Items { get; set; } =
        Array.Empty<ManualPaymentReconciliationDto>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
