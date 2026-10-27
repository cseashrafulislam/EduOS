namespace EduOS.Core.DTOs.Finance;

/// <summary>Current institutional receipt allocation and posting reconciliation; not an as-of ledger.</summary>
public sealed class FeePaymentReconciliationRowDto
{
    public Guid PaymentReference { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal GrossAllocated { get; set; }
    public decimal PaidRefunds { get; set; }
    public decimal NetAllocated { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Issues { get; set; } = string.Empty;
}

public sealed class FeePaymentReconciliationRegisterDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int PageExceptions { get; set; }
    public IReadOnlyList<FeePaymentReconciliationRowDto> Rows { get; set; } =
        Array.Empty<FeePaymentReconciliationRowDto>();
}
