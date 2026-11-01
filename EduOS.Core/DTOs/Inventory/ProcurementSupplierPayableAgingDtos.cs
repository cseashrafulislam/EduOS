namespace EduOS.Core.DTOs.Inventory;

/// <summary>Gross AP and source-linked posted GL settlements; not a bank-statement reconciliation.</summary>
public sealed class ProcurementSupplierPayableAgingRowDto
{
    public Guid InvoiceReference { get; set; }
    public Guid JournalReference { get; set; }
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public long CampusId { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal GrossPostedAmount { get; set; }
    public decimal PostedSettlementAmount { get; set; }
    public decimal ReservedUnpostedAmount { get; set; }
    public decimal? SourceLinkedOutstandingAmount { get; set; }
    public string SettlementIntegrityStatus { get; set; } = "SourceLinked";
    public int DaysOverdue { get; set; }
    public string AgingBucket { get; set; } = string.Empty;
    public bool ReversalPending { get; set; }
}
