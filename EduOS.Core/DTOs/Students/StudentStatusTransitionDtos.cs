using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Students;

/// <summary>Tenant-local status; enrollment identity and historical records are not rewritten.</summary>
public sealed class StudentStatusTransitionDto
{
    public Guid StudentReference { get; set; }
    public Guid CurrentEnrollmentReference { get; set; }
    public long CampusId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ChangeStudentOperationalStatusRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string TargetStatus { get; set; } = string.Empty;
    [Required, StringLength(500, MinimumLength = 10)] public string Reason { get; set; } = string.Empty;
}
