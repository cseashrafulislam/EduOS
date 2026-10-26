using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Students;

public sealed class StudentGuardianWorkspaceDto
{
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentRowVersion { get; set; } = string.Empty;
    public IReadOnlyList<StudentGuardianWorkspaceLinkDto> Guardians { get; set; } = Array.Empty<StudentGuardianWorkspaceLinkDto>();
}

public sealed class StudentGuardianWorkspaceLinkDto
{
    public long LinkId { get; set; }
    public Guid GuardianReference { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; }
    public bool CanReceiveEmail { get; set; }
    public bool IsAuthorizedPickup { get; set; }
    public bool IsFinancialContact { get; set; }
    public bool IsEmergencyContact { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateStudentGuardianWorkspaceRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public Guid StudentReference { get; set; }
    [Required] public string StudentRowVersion { get; set; } = string.Empty;
    [Required, StringLength(200)] public string FullName { get; set; } = string.Empty;
    [StringLength(30)] public string? Phone { get; set; }
    [EmailAddress, StringLength(200)] public string? Email { get; set; }
    [StringLength(150)] public string? Occupation { get; set; }
    [StringLength(1000)] public string? Address { get; set; }
    [Required, StringLength(50)] public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; } = true;
    public bool CanReceiveEmail { get; set; } = true;
    public bool IsAuthorizedPickup { get; set; }
    public bool IsFinancialContact { get; set; }
    public bool IsEmergencyContact { get; set; }
}

public sealed class UpdateStudentGuardianWorkspaceLinkDto
{
    [Required, StringLength(50)] public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; }
    public bool CanReceiveEmail { get; set; }
    public bool IsAuthorizedPickup { get; set; }
    public bool IsFinancialContact { get; set; }
    public bool IsEmergencyContact { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class LinkExistingStudentGuardianRequestDto
{
    [Required] public Guid StudentReference { get; set; }
    [Required] public Guid GuardianReference { get; set; }
    [Required] public string StudentRowVersion { get; set; } = string.Empty;
    [Required, StringLength(50)] public string RelationCode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool CanReceiveSms { get; set; }
    public bool CanReceiveEmail { get; set; }
    public bool IsAuthorizedPickup { get; set; }
    public bool IsFinancialContact { get; set; }
    public bool IsEmergencyContact { get; set; }
}

public sealed class UnlinkStudentGuardianWorkspaceRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, StringLength(500, MinimumLength = 10)] public string Reason { get; set; } = string.Empty;
}
