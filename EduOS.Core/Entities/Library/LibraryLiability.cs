using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Library;

/// <summary>One immutable, independently reviewed financial assessment per physical loan incident.</summary>
public sealed class LibraryLiability : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long BookIssueId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long CampusId { get; set; }
    public LibraryLiabilityKind Kind { get; set; }
    public LibraryLiabilityState State { get; set; } = LibraryLiabilityState.Assessed;
    public DateOnly AssessmentDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
    public long AssessedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(500)] public string? ReviewReason { get; set; }
    public long? StudentInvoiceId { get; set; }
    public long? LibraryFinePolicyId { get; set; }
    public int? OverdueDaysSnapshot { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? DailyRateSnapshot { get; set; }
    public int? GraceDaysSnapshot { get; set; }
}

/// <summary>Effective-dated campus policy. A new policy version is inserted; old versions are not rewritten.</summary>
public sealed class LibraryFinePolicy : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public int GraceDays { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DailyRate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? MaximumCharge { get; set; }
}

public enum LibraryLiabilityKind { Lost = 1, Damaged = 2, Overdue = 3 }
public enum LibraryLiabilityState { Assessed = 1, Waived = 2, Posted = 3, Reversed = 4 }
