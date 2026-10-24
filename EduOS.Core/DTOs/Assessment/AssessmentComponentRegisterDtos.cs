using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentComponentRegisterQueryDto
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    public long? AssessmentSubjectId { get; set; }
    [MaxLength(100)] public string? Search { get; set; }
}

public sealed class AssessmentComponentRegisterRowDto
{
    public long Id { get; set; }
    public long AssessmentId { get; set; }
    public long AssessmentSubjectId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public string BatchName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public AssessmentState State { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public decimal Weightage { get; set; }
    public bool IsMandatoryPass { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssessmentComponentRegisterPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<AssessmentComponentRegisterRowDto> Items { get; set; } =
        Array.Empty<AssessmentComponentRegisterRowDto>();
}
