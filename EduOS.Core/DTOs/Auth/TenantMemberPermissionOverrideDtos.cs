using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Auth;

/// <summary>Tenant-scoped exception to a member's role permissions; Inherit removes the exception.</summary>
public sealed class TenantMemberPermissionOverrideDto
{
    public long PermissionId { get; set; }
    public string PermissionCode { get; set; } = "";
    public string Mode { get; set; } = "Inherit";
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class SetTenantMemberPermissionOverrideRequestDto
{
    [Required] public string MembershipRowVersion { get; set; } = "";
    public string? OverrideRowVersion { get; set; }
    [Required, RegularExpression("^(Inherit|Allow|Deny)$")] public string Mode { get; set; } = "Inherit";
    [MaxLength(500)] public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
