namespace EduOS.Core.DTOs.Library;

/// <summary>Aggregate overdue loan counts for one tenant-owned library branch. No borrower identity is exposed.</summary>
public sealed class LibraryOverdueBranchDto
{
    public long BranchId { get; set; }
    public long CampusId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int TotalOverdue { get; set; }
    public int Days1To7 { get; set; }
    public int Days8To30 { get; set; }
    public int Days31Plus { get; set; }
    public DateOnly OldestDueDate { get; set; }
}
