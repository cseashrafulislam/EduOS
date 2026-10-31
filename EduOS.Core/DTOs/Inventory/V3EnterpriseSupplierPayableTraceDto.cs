namespace EduOS.Core.DTOs.Inventory;

// Canonical read-only evidence. Finance GL and Inventory remain the only balance sources.
public sealed class V3EnterpriseSupplierPayableTraceDto
{
    public Guid InvoiceReference { get; set; }
    public Guid PurchaseOrderReference { get; set; }
    public Guid SupplierReference { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string State { get; set; } = "";
    public long CampusId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal StockValue { get; set; }
    public decimal PriceVariance { get; set; }
    public int LineCount { get; set; }
    public IReadOnlyList<Guid> GoodsReceiptReferences { get; set; } = Array.Empty<Guid>();
    public string FinanceEvidenceStatus { get; set; } = "NoExplicitReference";
    public IReadOnlyList<V3EnterpriseJournalEvidenceDto> Journals { get; set; } =
        Array.Empty<V3EnterpriseJournalEvidenceDto>();
}
