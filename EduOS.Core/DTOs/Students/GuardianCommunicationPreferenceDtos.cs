using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Students;

/// <summary>Only the signed-in guardian's own notification preferences for an existing student relationship.</summary>
public sealed class GuardianCommunicationPreferenceDto
{
    public long LinkId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string RelationCode { get; set; } = string.Empty;
    public bool CanReceiveSms { get; set; }
    public bool CanReceiveEmail { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class UpdateGuardianCommunicationPreferenceDto
{
    public bool CanReceiveSms { get; set; }
    public bool CanReceiveEmail { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
