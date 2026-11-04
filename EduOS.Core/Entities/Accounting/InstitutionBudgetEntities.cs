using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Accounting;

// A budget is a proposed or approved planning snapshot, never another GL.
public sealed class InstitutionBudget : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [Required, MaxLength(20)] public string StateCode { get; set; } = "Submitted";
    public long SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewRequestId { get; set; }
    [MaxLength(800)] public string? ReviewReason { get; set; }
}

public sealed class InstitutionBudgetLine : BaseTenantEntity
{
    public long InstitutionBudgetId { get; set; }
    public long AccountId { get; set; }
    [Required, MaxLength(50)] public string AccountCodeSnapshot { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string AccountNameSnapshot { get; set; } = string.Empty;
    public AccountType AccountTypeSnapshot { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PlannedAmountBdt { get; set; }
}
