namespace EduOS.Core.DTOs.Inventory;

/// <summary>Gross canonical posted AP exposure, NOT a reconciled cash/bank outstanding balance.</summary>
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
    public int DaysOverdue { get; set; }
    public string AgingBucket { get; set; } = string.Empty;
    public bool ReversalPending { get; set; }
}
