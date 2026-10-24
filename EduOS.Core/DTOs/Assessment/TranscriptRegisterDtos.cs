using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class TranscriptRegisterQueryDto
{
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public sealed class TranscriptCandidateDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
}

public sealed class TranscriptRegisterRowDto
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string TranscriptNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class TranscriptRegisterPageDto
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<TranscriptRegisterRowDto> Items { get; set; } = Array.Empty<TranscriptRegisterRowDto>();
}
