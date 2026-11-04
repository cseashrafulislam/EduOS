using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

public sealed class CreateFacilityVendorServiceBillDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long FacilityWorkOrderId { get; set; }
    [Required, MaxLength(100)] public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal AmountBdt { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityVendorBillSubmitDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class FacilityVendorBillReviewDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
    public long AccountingPeriodId { get; set; }
    public long RepairsExpenseAccountId { get; set; }
    public long SupplierPayableAccountId { get; set; }
}

public sealed class FacilityVendorBillReversalRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public DateOnly ReversalDate { get; set; }
    public long AccountingPeriodId { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityVendorBillReversalReviewDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    [Required, MinLength(10), MaxLength(800)] public string Reason { get; set; } = string.Empty;
}

public sealed class FacilityVendorBillDto
{
    public Guid Reference { get; set; }
    public long FacilityWorkOrderId { get; set; }
    public long CampusId { get; set; }
    public long SupplierId { get; set; }
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal AmountBdt { get; set; }
    public string StateCode { get; set; } = string.Empty;
    public Guid? JournalReference { get; set; }
    public Guid? ReversalReference { get; set; }
    public string ReversalState { get; set; } = "None";
    public string? ReversalRowVersion { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public string? ReviewReason { get; set; }
    public long PreparedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
}
