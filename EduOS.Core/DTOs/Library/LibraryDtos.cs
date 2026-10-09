namespace EduOS.Core.DTOs.Library;

public class BookCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveBookCategoryRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class BookDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long? BookCategoryId { get; set; }
    public string? BookCategoryName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Publisher { get; set; }
    public string? ISBN { get; set; }
    public string? Edition { get; set; }
    public decimal? ReplacementPrice { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsActive { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveBookRequestDto
{
    public Guid? Reference { get; set; }
    public long? BookCategoryId { get; set; }
    [Required, MaxLength(300)] public string Title { get; set; } = string.Empty;
    [MaxLength(200)] public string? Author { get; set; }
    [MaxLength(200)] public string? Publisher { get; set; }
    [MaxLength(30)] public string? ISBN { get; set; }
    [MaxLength(100)] public string? Edition { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal? ReplacementPrice { get; set; }
    [MaxLength(500)] public string? CoverImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class BookCopyDto
{
    public long Id { get; set; }
    public Guid BookReference { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string AccessionNumber { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? ShelfNumber { get; set; }
    public BookCopyState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveBookCopyRequestDto
{
    public Guid BookReference { get; set; }
    [Required, MaxLength(100)] public string AccessionNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? Barcode { get; set; }
    [MaxLength(100)] public string? ShelfNumber { get; set; }
    public string? RowVersion { get; set; }
}

public class BookIssueDto
{
    public Guid Reference { get; set; }
    public long Id { get; set; }
    public long BookCopyId { get; set; }
    public string AccessionNumber { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly? ReturnDate { get; set; }
    public BookIssueState State { get; set; }
    public decimal FineAmount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class IssueBookRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long BookCopyId { get; set; }
    public Guid StudentReference { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
}

public class ReturnBookRequestDto
{
    public DateOnly ReturnDate { get; set; }
    [MaxLength(500)] public string? ConditionNote { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class BookReservationDto
{
    public long Id { get; set; }
    public Guid BookReference { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public DateTime ReservedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public BookReservationState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class ReserveBookRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid BookReference { get; set; }
    public Guid StudentReference { get; set; }
}
