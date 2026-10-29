using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Library;

public sealed class LibraryDigitalResourceDto
{
    public Guid Reference { get; set; }
    public Guid BookReference { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class LibraryDigitalUploadRequestDto
{
    [Required] public Guid RequestId { get; set; }
    [Required] public Guid BookReference { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
}

public sealed class LibraryDigitalRevokeRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
