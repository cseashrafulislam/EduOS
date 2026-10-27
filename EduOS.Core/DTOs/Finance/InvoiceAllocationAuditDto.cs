namespace EduOS.Core.DTOs.Finance;

// Gross receipt allocations are audit evidence, not GL clearance or refund approval.
public sealed class InvoiceAllocationAuditDto
{
    public Guid StudentReference { get; set; }
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "BDT";
    public decimal InvoiceTotal { get; set; }
    public decimal CurrentPaid { get; set; }
    public decimal CurrentDue { get; set; }
    public decimal GrossSuccessfulAllocations { get; set; }
    public decimal GrossToNetDifference { get; set; }
    public bool MatchesCurrentInvoice { get; set; }
    public int TotalAllocations { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<InvoiceAllocationAuditRowDto> Items { get; set; } = Array.Empty<InvoiceAllocationAuditRowDto>();
}
public sealed class InvoiceAllocationAuditRowDto
{
    public Guid ReceiptReference { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public PaymentState PaymentState { get; set; }
    public decimal AllocationAmount { get; set; }
    public long? JournalId { get; set; }
}
