namespace EduOS.Core.DTOs.LMS;

/// <summary>Safe file metadata for an existing assignment; never exposes private storage keys.</summary>
public sealed class AssignmentAttachmentDto
{
    public long Id { get; set; }
    public Guid AssignmentReference { get; set; }
    public Guid FileAssetReference { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public bool FileDownloadable { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class SaveAssignmentAttachmentRequestDto
{
    public Guid AssignmentReference { get; set; }
    public Guid FileAssetReference { get; set; }
    [MaxLength(200)] public string? Title { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class RemoveAssignmentAttachmentRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
