using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS;

/// <summary>Platform-only view of an aged, initiated AamarPay payment.</summary>
public sealed class GatewayPaymentRecoveryDto
{
    public long PaymentId { get; set; }
    public long TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal InvoiceDueAmount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public DateTime InitiatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public int AgeHours { get; set; }
    public string ExceptionCategory { get; set; } = "Awaiting";
    public string ExceptionReason { get; set; } = string.Empty;
    public bool CanReconcile { get; set; }
}
public sealed class PagedGatewayPaymentRecoveryDto
{
    public IReadOnlyList<GatewayPaymentRecoveryDto> Items { get; set; } = Array.Empty<GatewayPaymentRecoveryDto>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
public sealed class GatewayRecoveryRequestDto
{
    [Required, MaxLength(200)] public string RowVersion { get; set; } = string.Empty;
}


// Read-only, provider-authoritative investigation. Never supplies an instruction to settle funds.
public sealed class GatewayProviderInspectionDto
{
    public long PaymentId { get; set; }
    public long TenantId { get; set; }
    public DateTime InspectedAtUtc { get; set; }
    public string ProviderStatus { get; set; } = "Unconfirmed";
    public bool ProviderConfirmed { get; set; }
    public bool AmountMatches { get; set; }
    public bool CurrencyMatches { get; set; }
    public bool InvoiceBlocked { get; set; }
    public bool RequiresManualReview { get; set; }
    public string Finding { get; set; } = string.Empty;
    public string EvidenceSha256 { get; set; } = string.Empty;
    public long AuditId { get; set; }
}
