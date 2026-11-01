namespace EduOS.Core.DTOs.Inventory;

public sealed class CreateSupplierReturnCreditRequest
{
    public Guid ClientRequestId { get; set; }
    public string InvoiceRowVersion { get; set; } = "";
    public long SupplierInvoiceLineId { get; set; }
    public decimal Quantity { get; set; }
    public string Mode { get; set; } = "Automatic";
    public decimal ManualFinalCredit { get; set; }
    public decimal ManualCreditCorrection { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly ReturnDate { get; set; }
    public long? VarianceAccountId { get; set; }
    public long? RefundReceivableAccountId { get; set; }
    public string SupplierReturnReference { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class ReviewSupplierReturnCreditRequest
{
    public string RowVersion { get; set; } = "";
    public bool Approve { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SupplierReturnCreditDto
{
    public Guid Reference { get; set; }
    public Guid SupplierInvoiceReference { get; set; }
    public long SupplierInvoiceLineId { get; set; }
    public decimal Quantity { get; set; }
    public string Mode { get; set; } = "";
    public string State { get; set; } = "";
    public DateOnly ReturnDate { get; set; }
    public string SupplierReturnReference { get; set; } = "";
    public decimal StockValue { get; set; }
    public decimal AutomaticCredit { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal UnpaidCredit { get; set; }
    public decimal RefundReceivable { get; set; }
    public Guid? StockMovementReference { get; set; }
    public Guid? FinanceJournalReference { get; set; }
    public long? MakerId { get; set; }
    public long? ReviewerId { get; set; }
    public string RowVersion { get; set; } = "";
}
