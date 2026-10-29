namespace EduOS.Core.DTOs.Finance;

/// <summary>Current invoice-to-receipt reconciliation, not an as-of historical ledger.</summary>
public sealed class FeeReconciliationRowDto
{
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string CampusName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public decimal NetAllocatedAmount { get; set; }
    public decimal CreditNoteAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Issues { get; set; } = string.Empty;
}
public sealed class FeeReconciliationRegisterDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int PageExceptions { get; set; }
    public IReadOnlyList<FeeReconciliationRowDto> Rows { get; set; } = Array.Empty<FeeReconciliationRowDto>();
}
