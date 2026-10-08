using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Accounting;

public class Account : BaseTenantEntity
{
    public long? ParentAccountId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    // Compatibility alias only: ParentAccountId is the one authoritative hierarchy FK.
    // This property must remain unmapped; do not reintroduce a ParentId column.
    [NotMapped]
    public long? ParentId { get => ParentAccountId; set => ParentAccountId = value; }
    public AccountType AccountType { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public bool IsGroup { get; set; }
    public bool AllowsPosting { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class AccountingPeriod : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long? ClosedByUserId { get; set; }
}

public class BankAccount : BaseTenantEntity
{
    public long AccountId { get; set; }
    public long? CampusId { get; set; }
    [Required, MaxLength(150)] public string BankName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string AccountName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string AccountNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? BranchName { get; set; }
    [MaxLength(100)] public string? RoutingNumber { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public bool IsActive { get; set; } = true;
}

public class Journal : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid? IdempotencyKey { get; set; }
    [Required, MaxLength(50)] public string JournalNumber { get; set; } = string.Empty;
    public DateOnly JournalDate { get; set; }
    public long AccountingPeriodId { get; set; }
    public long? CampusId { get; set; }
    [MaxLength(100)] public string? ReferenceNumber { get; set; }
    [MaxLength(10)] public string CurrencyCode { get; set; } = "BDT";
    public JournalState State { get; set; } = JournalState.Draft;
    [MaxLength(100)] public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public long? ReversalOfJournalId { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long? PostedByUserId { get; set; }
    public DateTime? PostedAt { get; set; }
}

public class JournalLine : BaseTenantEntity
{
    public long JournalId { get; set; }
    public int LineNo { get; set; }
    public long AccountId { get; set; }
    public long? CampusId { get; set; }
    public long? StudentId { get; set; }
    public long? EmployeeId { get; set; }
    public long? AcademicProgramId { get; set; }
    public long? FeeHeadId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DebitAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal CreditAmount { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}
