using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Students;

/// <summary>Tenant-local guardian contact snapshot; not the global Person or login identity.</summary>
public sealed class GuardianContactProfileDto
{
    public Guid StudentReference { get; set; }
    public Guid GuardianReference { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Occupation { get; set; }
    public string? Address { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class UpdateGuardianContactProfileRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [MaxLength(150)] public string? Occupation { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    [Required, StringLength(500, MinimumLength = 10)] public string Reason { get; set; } = string.Empty;
}
