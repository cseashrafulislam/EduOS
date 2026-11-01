namespace EduOS.Core.DTOs.Inventory;

/// <summary>Finance approval instruction only: it does not execute a bank transfer or post GL.</summary>
public sealed class CreateSupplierSettlementInstructionRequest
{
    public Guid ClientRequestId { get; set; }
    public string InvoiceRowVersion { get; set; } = "";
    public long BankAccountId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly PlannedDate { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class ReviewSupplierSettlementInstructionRequest
{
    public string RowVersion { get; set; } = "";
    public bool Approve { get; set; }
    public string Reason { get; set; } = "";
}
/// <summary>Maker withdrawal of a submitted, unposted settlement reservation; no GL or bank posting.</summary>
public sealed class WithdrawSupplierSettlementInstructionRequest
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = "";
    public string Reason { get; set; } = "";
}
/// <summary>Confirms an independently approved bank disbursement in the canonical Finance GL;
/// this does not initiate an electronic bank transfer.</summary>
public sealed class PostSupplierSettlementRequest
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = "";
    public DateOnly ExecutedDate { get; set; }
    public string BankTransactionReference { get; set; } = "";
}
public sealed class RequestSupplierSettlementReversal
{
    public Guid ClientRequestId { get; set; }
    public string OriginalRowVersion { get; set; } = "";
    public long AccountingPeriodId { get; set; }
    public DateOnly ReversalDate { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class ReviewSupplierSettlementReversal
{
    public string RowVersion { get; set; } = "";
    public bool Approve { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class SupplierSettlementInstructionDto
{
    public Guid Reference { get; set; }
    public string State { get; set; } = "";
    public DateOnly PlannedDate { get; set; }
    public decimal Amount { get; set; }
    public long BankAccountId { get; set; }
    public long? MakerId { get; set; }
    public long? ReviewerId { get; set; }
    public long? PostedByUserId { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? BankTransactionReference { get; set; }
    public Guid? ReversalReference { get; set; }
    public string? ReversalState { get; set; }
    public string? ReversalRowVersion { get; set; }
    public string Reason { get; set; } = "";
    public string RowVersion { get; set; } = "";
}
public sealed class SupplierSettlementBankOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}
public sealed class SupplierSettlementOptionsDto
{
    public decimal InvoiceAmount { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public List<SupplierSettlementBankOptionDto> Banks { get; set; } = [];
    public List<ProcurementInvoicePeriodOptionDto> Periods { get; set; } = [];
}
