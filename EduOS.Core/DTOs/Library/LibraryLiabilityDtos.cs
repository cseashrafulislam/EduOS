using EduOS.Core.Entities.Library;

namespace EduOS.Core.DTOs.Library;

public sealed class LibraryLiabilityDto
{
    public Guid Reference { get; set; }
    public Guid IssueReference { get; set; }
    public string AccessionNumber { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public LibraryLiabilityKind Kind { get; set; }
    public LibraryLiabilityState State { get; set; }
    public decimal Amount { get; set; }
    public DateOnly AssessmentDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReviewReason { get; set; }
    public int? OverdueDays { get; set; }
    public decimal? DailyRate { get; set; }
    public Guid? InvoiceReference { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal? DueAmount { get; set; }
    public bool IsSettled { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class LibraryIncidentDto
{
    public Guid IssueReference { get; set; }
    public string AccessionNumber { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public LibraryLiabilityKind Kind { get; set; }
    public decimal? ReplacementPrice { get; set; }
    public decimal? CalculatedAmount { get; set; }
    public int? OverdueDays { get; set; }
    public string? PolicyStatus { get; set; }
    public bool Assessed { get; set; }
}

public sealed class AssessLibraryLiabilityRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid IssueReference { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999.99")] public decimal Amount { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class SaveLibraryFinePolicyDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    [Range(0, 36500)] public int GraceDays { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal DailyRate { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999.99")] public decimal? MaximumCharge { get; set; }
}

public sealed class LibraryFinePolicyDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public int GraceDays { get; set; }
    public decimal DailyRate { get; set; }
    public decimal? MaximumCharge { get; set; }
}

public sealed class RequestLibraryReversalDto
{
    public Guid ClientRequestId { get; set; }
    public long AccountingPeriodId { get; set; }
    public DateOnly ReversalDate { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReviewLibraryReversalDto
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? RejectionReason { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LibraryReversalDto
{
    public Guid Reference { get; set; }
    public Guid LiabilityReference { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public int State { get; set; }
    public DateOnly ReversalDate { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReviewLibraryLiabilityRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Waive { get; set; }
    [MaxLength(500)] public string? WaiverReason { get; set; }
    public long? AccountingPeriodId { get; set; }
    public long? FeeHeadId { get; set; }
}

public sealed class LibraryPostingOptionDto
{
    public long Id { get; set; }
    public string Label { get; set; } = string.Empty;
}
