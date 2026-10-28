using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentMarkChangeQueryDto : AssessmentScopeDto
{
    [Range(1, long.MaxValue)] public long AssessmentSubjectId { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
}
public sealed class AssessmentMarkChangeDto
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public long? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
}
public sealed class AssessmentMarkChangePageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<AssessmentMarkChangeDto> Items { get; set; } =
        Array.Empty<AssessmentMarkChangeDto>();
}
