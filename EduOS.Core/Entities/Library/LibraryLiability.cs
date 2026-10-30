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
}

public enum LibraryLiabilityKind { Lost = 1, Damaged = 2 }
public enum LibraryLiabilityState { Assessed = 1, Waived = 2, Posted = 3 }
