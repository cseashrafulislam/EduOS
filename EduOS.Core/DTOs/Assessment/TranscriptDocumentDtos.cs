namespace EduOS.Core.DTOs.Assessment;

/// <summary>Stable, issue-time academic evidence. Never rebuilt from editable marks.</summary>
public sealed class TranscriptSnapshotDocumentDto
{
    public int SchemaVersion { get; set; } = 1;
    public long TenantId { get; set; }
    public string IssuerName { get; set; } = string.Empty;
    public long StudentId { get; set; }
    public Guid TranscriptReference { get; set; }
    public string TranscriptNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public long IssuedByUserId { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public TranscriptAcademicRecordDto AcademicRecord { get; set; } = new();
}
