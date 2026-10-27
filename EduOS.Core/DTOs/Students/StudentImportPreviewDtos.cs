namespace EduOS.Core.DTOs.Students;

/// <summary>Preflight only: no learner identity or enrollment is created by this workflow.</summary>
public sealed class StudentImportPreviewDto
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public bool HasMoreErrors { get; set; }
    public List<StudentImportRowIssueDto> Errors { get; set; } = new();
}

public sealed class StudentImportRowIssueDto
{
    public int RowNumber { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
}
