using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Library;

/// <summary>Privacy-safe copy/loan discrepancy. No borrower or global identity fields.</summary>
public sealed class LibraryCopyIntegrityDto
{
    public long CopyId { get; set; }
    public string AccessionNumber { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public string? BranchCode { get; set; }
    public BookCopyState CopyState { get; set; }
    public int OpenLoanCount { get; set; }
    public bool MissingBook { get; set; }
    public bool MissingBranch { get; set; }
    public bool StateMismatch { get; set; }
    public bool MultipleOpenLoans { get; set; }
}
