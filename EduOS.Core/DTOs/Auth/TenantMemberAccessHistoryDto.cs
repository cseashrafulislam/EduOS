namespace EduOS.Core.DTOs.Auth;

/// <summary>Tenant-admin readback of canonical membership access changes.</summary>
public sealed class TenantMemberAccessHistoryDto
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
}
