namespace EduOS.Core.DTOs.Finance;

public class FeeHeadDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public FeeFrequencyType DefaultFrequency { get; set; }
    public bool IsRefundable { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveFeeHeadRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public FeeFrequencyType DefaultFrequency { get; set; } = FeeFrequencyType.Monthly;
    public bool IsRefundable { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class FeeStructureDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public long? AcademicProgramId { get; set; }
    public string? AcademicProgramName { get; set; }
    public long? AcademicLevelId { get; set; }
    public string? AcademicLevelName { get; set; }
    public long? AcademicBatchId { get; set; }
    public string? AcademicBatchName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "BDT";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<FeeStructureLineDto> Lines { get; set; } = Array.Empty<FeeStructureLineDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveFeeStructureRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicProgramId { get; set; }
    public long? AcademicLevelId { get; set; }
    public long? AcademicBatchId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<SaveFeeStructureLineRequestDto> Lines { get; set; } = Array.Empty<SaveFeeStructureLineRequestDto>();
    public string? RowVersion { get; set; }
}

public class FeeStructureLineDto
{
    public long Id { get; set; }
    public long FeeHeadId { get; set; }
    public string FeeHeadName { get; set; } = string.Empty;
    public FeeFrequencyType Frequency { get; set; }
    public decimal Amount { get; set; }
    public int? DueDayOfMonth { get; set; }
    public bool IsMandatory { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveFeeStructureLineRequestDto
{
    public long? Id { get; set; }
    public long FeeHeadId { get; set; }
    public FeeFrequencyType Frequency { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal Amount { get; set; }
    [Range(1, 31)] public int? DueDayOfMonth { get; set; }
    public bool IsMandatory { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class StudentInvoiceDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public InvoiceState State { get; set; }
    public DateTime? PaidAt { get; set; }
    public IReadOnlyList<StudentInvoiceLineDto> Lines { get; set; } = Array.Empty<StudentInvoiceLineDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class GenerateStudentInvoiceRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public long FeeStructureId { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
}

public class StudentInvoiceLineDto
{
    public long Id { get; set; }
    public long FeeHeadId { get; set; }
    public string FeeHeadName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal NetAmount { get; set; }
}

public class StudentPaymentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public PaymentState State { get; set; }
    public long? BankAccountId { get; set; }
    public string? BankAccountName { get; set; }
    public string? ExternalReference { get; set; }
    public long ReceivedByUserId { get; set; }
    public long? JournalId { get; set; }
    public IReadOnlyList<PaymentAllocationDto> Allocations { get; set; } = Array.Empty<PaymentAllocationDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateStudentPaymentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal Amount { get; set; }
    public long? BankAccountId { get; set; }
    [MaxLength(150)] public string? ExternalReference { get; set; }
    public IReadOnlyList<CreatePaymentAllocationRequestDto> Allocations { get; set; } = Array.Empty<CreatePaymentAllocationRequestDto>();
}

public class PaymentAllocationDto
{
    public long Id { get; set; }
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class CreatePaymentAllocationRequestDto
{
    public Guid InvoiceReference { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal Amount { get; set; }
}

public class DiscountRuleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? FeeHeadId { get; set; }
    public string? FeeHeadName { get; set; }
    public bool IsPercentage { get; set; }
    public decimal Value { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveDiscountRuleRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public long? FeeHeadId { get; set; }
    public bool IsPercentage { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal Value { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class StudentDiscountDto
{
    public long Id { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public long DiscountRuleId { get; set; }
    public string DiscountRuleName { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long ApprovedByUserId { get; set; }
    public string? Reason { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AssignStudentDiscountRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public long DiscountRuleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    [MaxLength(1000)] public string? Reason { get; set; }
}

public class FineRuleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? FeeHeadId { get; set; }
    public string? FeeHeadName { get; set; }
    public decimal Amount { get; set; }
    public bool IsDaily { get; set; }
    public int GraceDays { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveFineRuleRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public long? FeeHeadId { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal Amount { get; set; }
    public bool IsDaily { get; set; }
    [Range(0, 3650)] public int GraceDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class StudentFineDto
{
    public long Id { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public long FineRuleId { get; set; }
    public string FineRuleName { get; set; } = string.Empty;
    public DateOnly FineDate { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public bool IsWaived { get; set; }
    public long? WaivedByUserId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class ApplyStudentFineRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public long FineRuleId { get; set; }
    public DateOnly FineDate { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public class WaiveStudentFineRequestDto
{
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class RefundDto
{
    public long Id { get; set; }
    public Guid StudentPaymentReference { get; set; }
    public decimal Amount { get; set; }
    public RefundState State { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long RequestedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public long? ReversalJournalId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateRefundRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentPaymentReference { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999")] public decimal Amount { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}

public class ReviewRefundRequestDto
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
