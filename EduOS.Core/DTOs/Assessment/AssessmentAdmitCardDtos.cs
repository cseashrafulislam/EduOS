using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentAdmitCardQueryDto : AssessmentScopeDto
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
    [MaxLength(100)] public string? Search { get; set; }
}

public sealed class AssessmentAdmitCardScheduleDto
{
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? RoomName { get; set; }
}

public sealed class AssessmentAdmitCardDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public IReadOnlyList<AssessmentAdmitCardScheduleDto> Sessions { get; set; } = [];
}

public sealed class AssessmentAdmitCardPageDto
{
    public string AssessmentName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<AssessmentAdmitCardDto> Items { get; set; } = [];
}
