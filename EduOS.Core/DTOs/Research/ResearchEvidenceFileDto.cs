namespace EduOS.Core.DTOs.Research;

public sealed class ResearchEvidenceFileDto
{
    public Guid Reference { get; set; }
    public int RevisionNo { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}
