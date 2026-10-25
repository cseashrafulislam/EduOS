namespace EduOS.Core.DTOs.Student;

/// <summary>Tenant-private, immutable lifecycle facts for an authorized institution administrator.</summary>
public sealed class StudentLifecycleTimelineDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public bool HasMore { get; set; }
    public IReadOnlyList<StudentLifecycleEventDto> Events { get; set; } = Array.Empty<StudentLifecycleEventDto>();
}

public sealed class StudentLifecycleEventDto
{
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Detail { get; set; }
    public string? Reason { get; set; }
    public long ActorUserId { get; set; }
}
