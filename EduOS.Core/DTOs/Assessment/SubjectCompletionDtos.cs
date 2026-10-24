using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;
namespace EduOS.Core.DTOs.Assessment;
public sealed class SubjectCompletionQueryDto
{
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
}
public sealed class SubjectCompletionRowDto
{
    public long RegistrationId { get; set; }
    public long AcademicBatchId { get; set; }
    public string RollNo { get; set; } = "";
    public string StudentCode { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public SubjectRegistrationState State { get; set; }
    public bool CanComplete { get; set; }
    public string StatusMessage { get; set; } = "";
    public string RowVersion { get; set; } = "";
}
public sealed class SubjectCompletionPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<SubjectCompletionRowDto> Items { get; set; } = Array.Empty<SubjectCompletionRowDto>();
}
public sealed class CompleteSubjectRequestDto
{
    [Required] public string RowVersion { get; set; } = "";
    [Required, MaxLength(500)] public string Reason { get; set; } = "";
}
