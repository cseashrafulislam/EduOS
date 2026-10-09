namespace EduOS.Core.DTOs.Library;

public sealed class LibraryBranchDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Location { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveLibraryBranchRequestDto
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class ChangeBookCopyStateRequestDto
{
    public BookCopyState State { get; set; }
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
