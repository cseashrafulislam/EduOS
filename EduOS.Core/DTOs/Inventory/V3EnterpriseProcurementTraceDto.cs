namespace EduOS.Core.DTOs.Inventory;

// Read-only purchase commitment provenance. No receipt, stock or Finance
// posting is inferred from a requisition, purchase order or inventory item.
public sealed class V3EnterpriseProcurementTraceDto
{
    public Guid OrderReference { get; set; }
    public string OrderNumber { get; set; } = "";
    public string State { get; set; } = "";
    public long CampusId { get; set; }
    public Guid RequisitionReference { get; set; }
    public Guid SupplierReference { get; set; }
    public decimal TotalAmount { get; set; }
    // Requisition-approved ceiling and negotiated difference, not a payable
    // or remaining-to-receive balance. Both values are in the PO currency.
    public decimal ApprovedBudget { get; set; }
    public decimal BudgetVariance { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public int LineCount { get; set; }
    public string EvidenceStatus { get; set; } = "";
    public IReadOnlyList<V3EnterpriseProcurementTraceLineDto> Lines { get; set; } =
        Array.Empty<V3EnterpriseProcurementTraceLineDto>();
}

public sealed class V3EnterpriseProcurementTraceLineDto
{
    public int LineNo { get; set; }
    public long InventoryItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount => Quantity * UnitPrice;
}
