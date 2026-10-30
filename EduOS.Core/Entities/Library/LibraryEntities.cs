using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Library;

public class BookCategory : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Book : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long? BookCategoryId { get; set; }
    [Required, MaxLength(300)] public string Title { get; set; } = string.Empty;
    [MaxLength(200)] public string? Author { get; set; }
    [MaxLength(200)] public string? Publisher { get; set; }
    [MaxLength(30)] public string? ISBN { get; set; }
    [MaxLength(100)] public string? Edition { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? ReplacementPrice { get; set; }
    [MaxLength(500)] public string? CoverImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BookCopy : BaseTenantEntity
{
    public long BookId { get; set; }
    public long? LibraryBranchId { get; set; }
    [Required, MaxLength(100)] public string AccessionNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? Barcode { get; set; }
    [MaxLength(100)] public string? ShelfNumber { get; set; }
    public BookCopyState State { get; set; } = BookCopyState.Available;
}

public class BookIssue : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long BookCopyId { get; set; }
    // Exactly one tenant-owned borrower; nullable to preserve historical student loans
    // while allowing employee circulation in the same canonical copy/loan ledger.
    public long? StudentId { get; set; }
    public long? EmployeeId { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? ReturnDate { get; set; }
    // Immutable price observed when the loan starts; never reprice historical losses from today's catalogue.
    [Column(TypeName = "decimal(18,2)")] public decimal? ReplacementPriceSnapshot { get; set; }
    public BookIssueState State { get; set; } = BookIssueState.Issued;
    [Column(TypeName = "decimal(18,2)")] public decimal FineAmount { get; set; }
    public long IssuedByUserId { get; set; }
    public long? ReturnedByUserId { get; set; }
}

public class BookReservation : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long BookId { get; set; }
    public long StudentId { get; set; }
    public DateTime ReservedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public BookReservationState State { get; set; } = BookReservationState.Pending;
}

public class LibraryBranch : BaseTenantEntity
{
    public long CampusId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
}
