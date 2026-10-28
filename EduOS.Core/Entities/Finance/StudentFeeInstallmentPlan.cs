using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance;

// Gross contractual schedule, never a receipt, invoice or posted obligation.
// Immutable plan/line rows; state is derived only from append-only approval events.
public sealed class StudentFeeInstallmentPlan : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long FeeStructureId { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public DateOnly StartMonth { get; set; }
    public DateOnly EndMonth { get; set; }
    [Required, MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    [Column(TypeName = "decimal(18,2)")] public decimal GrossAmount { get; set; }
    public int LineCount { get; set; }
    public long PreparedByUserId { get; set; }
}

public sealed class StudentFeeInstallmentPlanLine : BaseTenantEntity
{
    public long StudentFeeInstallmentPlanId { get; set; }
    public long FeeHeadId { get; set; }
    public DateOnly MonthStart { get; set; }
    public DateOnly DueDate { get; set; }
    [Required, MaxLength(50)] public string FeeHeadCodeSnapshot { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string FeeHeadNameSnapshot { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal GrossAmount { get; set; }
}

public sealed class StudentFeeInstallmentPlanEvent : BaseTenantEntity
{
    public long StudentFeeInstallmentPlanId { get; set; }
    public Guid ClientRequestId { get; set; }
    public int ActionCode { get; set; } // 2 submit, 3 independently approve, 4 cancel
    public long ActorUserId { get; set; }
    public DateTime EventAt { get; set; } = DateTime.UtcNow;
    [MaxLength(250)] public string? Reason { get; set; }
}
