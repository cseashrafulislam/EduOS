namespace EduOS.Core.DTOs.Auth;

public sealed class UserCampusAccessDto
{
    public long CampusId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SetMembershipCampusAccessRequestDto
{
    public IReadOnlyList<long> CampusIds { get; set; } = Array.Empty<long>();
    public long? DefaultCampusId { get; set; }
    [Required] public string MembershipRowVersion { get; set; } = string.Empty;
}

public sealed class TenantInvitationDto
{
    public Guid Reference { get; set; }
    public string Email { get; set; } = string.Empty;
    public long? RoleId { get; set; }
    public long? CampusId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class CreateTenantInvitationRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = string.Empty;
    public long? RoleId { get; set; }
    public long? CampusId { get; set; }
}

public sealed class AcceptTenantInvitationRequestDto
{
    [Required] public string Token { get; set; } = string.Empty;
}
