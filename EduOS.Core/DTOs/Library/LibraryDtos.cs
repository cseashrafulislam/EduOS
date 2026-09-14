using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Library;

public sealed class LibraryBookDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? ISBN { get; set; }
    public string? Category { get; set; }
    public string? ShelfNo { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
}

public sealed class IssueBookDto
{
    public Guid ClientRequestId { get; set; }
    public Guid BookReference { get; set; }
    public Guid? StudentReference { get; set; }
    public long? EmployeeId { get; set; }
    public DateTime DueDate { get; set; }
}

public sealed class ReturnBookDto
{
    [Range(typeof(decimal), "0", "999999999")] public decimal FineAmount { get; set; }
    [StringLength(20)] public string Action { get; set; } = "Returned";
}

public sealed class LibraryIssueDto
{
    public Guid Reference { get; set; }
    public Guid BookReference { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string BorrowerType { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ActualReturnDate { get; set; }
    public decimal FineAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
