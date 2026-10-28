namespace EduOS.Core.DTOs.Assessment;

/// <summary>Public credential status only; never includes private learner or marks data.</summary>
public sealed class PublicTranscriptVerificationDto
{
    public string IssuerName { get; set; } = string.Empty;
    public string TranscriptNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public string Status { get; set; } = string.Empty;
}
