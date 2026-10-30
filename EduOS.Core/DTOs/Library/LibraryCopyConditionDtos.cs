using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Library;

/// <summary>Staff-only physical copy condition; borrower identity is deliberately excluded.</summary>
public sealed class LibraryCopyConditionDto
{
    public long CopyId { get; set; }
    public string AccessionNumber { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public string? BranchCode { get; set; }
    public BookCopyState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public Guid? OpenIssueReference { get; set; }
    public string? OpenIssueRowVersion { get; set; }
    public int OpenIssueCount { get; set; }
}

public sealed class ChangeLibraryCopyConditionRequestDto
{
    public Guid ClientRequestId { get; set; }
    public BookCopyState TargetState { get; set; }
    [Required, MaxLength(300)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
    public string? OpenIssueRowVersion { get; set; }
}

public sealed class LibraryCopyConditionHistoryDto
{
    public string PreviousState { get; set; } = string.Empty;
    public string NewState { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
