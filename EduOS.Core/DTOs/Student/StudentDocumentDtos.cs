using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Student;

public sealed class StudentDocumentDto
{
    public Guid Reference { get; set; }
    public Guid FileReference { get; set; }
    public string DocumentTypeCode { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class StudentDocumentTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
public sealed class AttachStudentDocumentDto
{
    public Guid FileReference { get; set; }
    [Required, MaxLength(100)] public string DocumentTypeCode { get; set; } = string.Empty;
    [MaxLength(250)] public string? Title { get; set; }
    public DateTime? ExpiresAt { get; set; }
    [Required] public string StudentRowVersion { get; set; } = string.Empty;
}
public sealed class DeactivateStudentDocumentDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class PortalStudentDocumentDto
{
    public Guid Reference { get; set; }
    public string DocumentTypeCode { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}
