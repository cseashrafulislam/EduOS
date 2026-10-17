namespace EduOS.Core.DTOs.Communication;

public sealed class MessageAttachmentDto
{
    public Guid Reference { get; set; }
    public long MessageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}

public sealed class MessageAttachmentListingDto
{
    public bool CanAttach { get; set; }
    public IReadOnlyList<MessageAttachmentDto> Items { get; set; } = Array.Empty<MessageAttachmentDto>();
}
