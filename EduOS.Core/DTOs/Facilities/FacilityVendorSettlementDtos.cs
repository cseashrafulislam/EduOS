using EduOS.Core.Common;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

/// <summary>Creates a reservation, not an electronic bank transfer or posted GL.</summary>
public sealed class FacilityVendorPaymentInstructionRequest
{
    public Guid ClientRequestId { get; set; }
    [Required] public string BillRowVersion { get; set; } = string.Empty;
    public long BankAccountId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly PlannedDate { get; set; }
    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal Amount { get; set; }
    [Required, MinLength(10), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
public sealed class FacilityVendorPaymentReviewRequest
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    [Required, MinLength(10), MaxLength(400)]
    public string Reason { get; set; } = string.Empty;
}
public sealed class FacilityVendorPaymentPostRequest
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public DateOnly ExecutedDate { get; set; }
    [Required, MinLength(6), MaxLength(100)]
    public string BankTransactionReference { get; set; } = string.Empty;
}
public sealed class FacilityVendorPaymentReversalRequest
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public long AccountingPeriodId { get; set; }
    public DateOnly ReversalDate { get; set; }
    [Required, MinLength(6), MaxLength(100)]
    public string BankReversalReference { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
public sealed class FacilityVendorPaymentReversalReviewRequest
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    [Required, MinLength(10), MaxLength(400)]
    public string Reason { get; set; } = string.Empty;
}
public sealed class FacilityVendorPaymentDto
{
    public Guid Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public long BankAccountId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public long? MakerId { get; set; }
    public long? ReviewerId { get; set; }
    public long? PostedByUserId { get; set; }
    public string? BankTransactionReference { get; set; }
    public bool BankStatementCleared { get; set; }
    public string? ClearedStatementIdentifier { get; set; }
    public Guid? ReversalReference { get; set; }
    public string? ReversalState { get; set; }
    public string? ReversalRowVersion { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class FacilityVendorPayableAgingDto
{
    public Guid BillReference { get; set; }
    public long SupplierId { get; set; }
    public long CampusId { get; set; }
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal PostedNetPayments { get; set; }
    public decimal PendingPayments { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int DaysOverdue { get; set; }
    public string StateCode { get; set; } = string.Empty;
}
/// <summary>Consolidated read-only supplier aging across stock and service AP.</summary>
public sealed class FacilitySupplierAgingRowDto
{
    public string InvoiceSource { get; set; } = string.Empty;
    public Guid InvoiceReference { get; set; }
    public long SupplierId { get; set; }
    public long CampusId { get; set; }
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal InvoiceAmount { get; set; }
    public decimal NetPostedPayments { get; set; }
    public decimal PostedSupplierCredits { get; set; }
    public decimal OpenBalance { get; set; }
    public int DaysOverdue { get; set; }
    public bool InvoiceReversedAsOf { get; set; }
    public long? PayableLedgerAccountId { get; set; }
    public decimal? PostedGlPayableBalance { get; set; }
    public decimal? SubledgerVsGlDifference { get; set; }
    public string GlReconciliationStatus { get; set; } = "NotChecked";
}
public sealed class FacilitySupplierAgingTotalsDto
{
    public DateOnly AsOf { get; set; }
    public decimal OutstandingOnPage { get; set; }
    public decimal CurrentOnPage { get; set; }
    public decimal Days1To30OnPage { get; set; }
    public decimal Days31To60OnPage { get; set; }
    public decimal Days61To90OnPage { get; set; }
    public decimal Over90DaysOnPage { get; set; }
    public PagedResult<FacilitySupplierAgingRowDto> Page { get; set; } = new();
    public int GlExceptionCountOnPage { get; set; }
    public decimal? PostedGlPayableOnPage { get; set; }
    public decimal? SubledgerVsGlDifferenceOnPage { get; set; }
}

/// <summary>Full authorized supplier AP control, not a sum of the displayed invoice page.</summary>
public sealed class InstitutionalSupplierApControlDto
{
    public DateOnly AsOf { get; set; }
    public long? CampusId { get; set; }
    public int InvoiceCount { get; set; }
    public int InvalidSourceCount { get; set; }
    public decimal SubledgerOutstanding { get; set; }
    public decimal LinkedPostedGlBalance { get; set; }
    public decimal ControlAccountGlBalance { get; set; }
    public decimal UnattributedGlBalance { get; set; }
    public decimal SubledgerVsControlDifference { get; set; }
    public bool Reconciled { get; set; }
    public IReadOnlyList<InstitutionalSupplierApControlAccountDto> Accounts { get; set; } =
        Array.Empty<InstitutionalSupplierApControlAccountDto>();
}

public sealed class InstitutionalSupplierApControlAccountDto
{
    public long AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int SourceCount { get; set; }
    public int ExceptionCount { get; set; }
    public decimal SubledgerOutstanding { get; set; }
    public decimal LinkedPostedGlBalance { get; set; }
    public decimal ControlAccountGlBalance { get; set; }
    public decimal UnattributedGlBalance => ControlAccountGlBalance - LinkedPostedGlBalance;
    public decimal Difference => SubledgerOutstanding - ControlAccountGlBalance;
    public bool Reconciled => ExceptionCount == 0 && Difference == 0m && UnattributedGlBalance == 0m;
}

public sealed class FacilityVendorPaymentOptionsDto
{
    public decimal InvoiceAmount { get; set; }
    public decimal PostedNetPayments { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public IReadOnlyList<FacilityVendorPaymentBankChoiceDto> Banks { get; set; } = [];
    public IReadOnlyList<FacilityVendorBillingPeriodOptionDto> Periods { get; set; } = [];
}
public sealed class FacilityVendorPaymentBankChoiceDto
{
    public long BankAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
}
