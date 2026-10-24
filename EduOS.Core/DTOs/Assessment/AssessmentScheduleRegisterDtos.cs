using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentScheduleRegisterQueryDto
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    public long? AssessmentId { get; set; }
    public long? AssessmentSubjectId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

public sealed class AssessmentScheduleRegisterRowDto
{
    public long Id { get; set; }
    public long AssessmentId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public long AssessmentSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public AssessmentState State { get; set; }
    public DateOnly AssessmentDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public long? RoomId { get; set; }
    public string? RoomName { get; set; }
    public string? Instructions { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssessmentScheduleRegisterPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<AssessmentScheduleRegisterRowDto> Items { get; set; } =
        Array.Empty<AssessmentScheduleRegisterRowDto>();
}

public sealed class AssessmentScheduleRoomDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
