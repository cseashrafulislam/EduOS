namespace EduOS.Core.DTOs.Finance;

public sealed class CreateStudentFeeInstallmentPlanDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    public long AcademicBatchId { get; set; }
    public long FeeStructureId { get; set; }
    public DateOnly StartMonth { get; set; }
    public int Months { get; set; }
}

public sealed class StudentFeeInstallmentTransitionDto
{
    public Guid ClientRequestId { get; set; }
    public string? RowVersion { get; set; }
    public string? Reason { get; set; }
}

public sealed class IssueStudentFeeInstallmentMonthDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly MonthStart { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class StudentFeeInstallmentMonthInvoiceDto
{
    public Guid PlanReference { get; set; }
    public DateOnly MonthStart { get; set; }
    public Guid InvoiceReference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public bool Existing { get; set; }
}

public sealed class StudentFeeInstallmentPlanLineDto
{
    public DateOnly MonthStart { get; set; }
    public DateOnly DueDate { get; set; }
    public long FeeHeadId { get; set; }
    public string FeeHeadCode { get; set; } = string.Empty;
    public string FeeHeadName { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public Guid? InvoiceReference { get; set; }
    public string? InvoiceNumber { get; set; }
}

public sealed class StudentFeeInstallmentPlanEventDto
{
    public int ActionCode { get; set; }
    public long ActorUserId { get; set; }
    public DateTime EventAt { get; set; }
    public string? Reason { get; set; }
}

public sealed class StudentFeeInstallmentPlanDto
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public long AcademicBatchId { get; set; }
    public long FeeStructureId { get; set; }
    public DateOnly StartMonth { get; set; }
    public DateOnly EndMonth { get; set; }
    public string CurrencyCode { get; set; } = "BDT";
    public decimal GrossAmount { get; set; }
    public int LineCount { get; set; }
    public int StateCode { get; set; }
    public long PreparedByUserId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<StudentFeeInstallmentPlanLineDto> Lines { get; set; } = [];
    public IReadOnlyList<StudentFeeInstallmentPlanEventDto> Events { get; set; } = [];
}
