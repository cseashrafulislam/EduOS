using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Inventory;

/// <summary>Partial supplier goods return and its source-linked Finance credit note.
/// Physical stock and GL change together only on independent approval.</summary>
public sealed class ProcurementSupplierReturnCredit : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long SupplierInvoiceId { get; set; }
    public long SupplierInvoiceLineId { get; set; }
    public long CampusId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly ReturnDate { get; set; }
    [Required, MaxLength(100)] public string SupplierReturnReference { get; set; } = "";
    [Required, MaxLength(30)] public string Mode { get; set; } = "Automatic";
    [Required, MaxLength(25)] public string StateCode { get; set; } = "Submitted";
    [Column(TypeName = "decimal(18,6)")] public decimal Quantity { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal StockValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CreditAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AutomaticCreditAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal UnpaidCreditAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal RefundReceivableAmount { get; set; }
    public long StockAccountId { get; set; }
    public long PayableAccountId { get; set; }
    public long? VarianceAccountId { get; set; }
    public long? RefundReceivableAccountId { get; set; }
    public long? StockMovementId { get; set; }
    public long? JournalId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    [Required, MaxLength(800)] public string Reason { get; set; } = "";
    [MaxLength(800)] public string? ReviewReason { get; set; }
}
