using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance;

public class FeeHead : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public FeeFrequencyType DefaultFrequency { get; set; } = FeeFrequencyType.Monthly;
    public long? IncomeAccountId { get; set; }
    public long? ReceivableAccountId { get; set; }
    public bool IsRefundable { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FeeStructure : BaseTenantEntity
{
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicProgramId { get; set; }
    public long? AcademicLevelId { get; set; }
    public long? AcademicBatchId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FeeStructureLine : BaseTenantEntity
{
    public long FeeStructureId { get; set; }
    public long FeeHeadId { get; set; }
    public FeeFrequencyType Frequency { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public int? DueDayOfMonth { get; set; }
    public bool IsMandatory { get; set; } = true;
}

public class StudentInvoice : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentEnrollmentId { get; set; }
    [Required, MaxLength(50)] public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal FineAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TotalAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PaidAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DueAmount { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public InvoiceState State { get; set; } = InvoiceState.Issued;
    public long? JournalId { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class StudentInvoiceLine : BaseTenantEntity
{
    public long StudentInvoiceId { get; set; }
    public long FeeHeadId { get; set; }
    [MaxLength(50)] public string? FeeHeadCodeSnapshot { get; set; }
    [MaxLength(150)] public string? FeeHeadNameSnapshot { get; set; }
    [Required, MaxLength(250)] public string Description { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DiscountAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal FineAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetAmount { get; set; }
}

public class StudentPayment : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    [Required, MaxLength(50)] public string ReceiptNumber { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public PaymentState State { get; set; } = PaymentState.Successful;
    public long? BankAccountId { get; set; }
    [MaxLength(100)] public string? ProviderCode { get; set; }
    [MaxLength(150)] public string? ProviderTransactionId { get; set; }
    [MaxLength(150)] public string? ExternalReference { get; set; }
    [MaxLength(200)] public string? PayerName { get; set; }
    [MaxLength(30)] public string? PayerPhone { get; set; }
    public DateTime InitiatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    [MaxLength(1000)] public string? FailureReason { get; set; }
    public long ReceivedByUserId { get; set; }
    public long? JournalId { get; set; }
}

public class PaymentAllocation : BaseTenantEntity
{
    public long StudentPaymentId { get; set; }
    public long StudentInvoiceId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}

public class DiscountRule : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public long? FeeHeadId { get; set; }
    public bool IsPercentage { get; set; }
    [Column(TypeName = "decimal(18,4)")] public decimal Value { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StudentDiscount : BaseTenantEntity
{
    public long StudentEnrollmentId { get; set; }
    public long DiscountRuleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long ApprovedByUserId { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FineRule : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public long? FeeHeadId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public bool IsDaily { get; set; }
    public int GraceDays { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StudentFine : BaseTenantEntity
{
    public long StudentEnrollmentId { get; set; }
    public long FineRuleId { get; set; }
    public DateOnly FineDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public bool IsWaived { get; set; }
    public long? WaivedByUserId { get; set; }
}

public class Refund : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long StudentPaymentId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public RefundState State { get; set; } = RefundState.Draft;
    [MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public long? ReversalJournalId { get; set; }
}

public class RefundAllocation : BaseTenantEntity
{
    public long RefundId { get; set; }
    public long PaymentAllocationId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
}
