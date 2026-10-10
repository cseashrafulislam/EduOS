namespace EduOS.Core.DTOs.Auth;

public class UserSummaryDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long? PersonId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PhotoUrl { get; set; }
    public string PreferredLanguage { get; set; } = "bn-BD";
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class UpdateUserProfileRequestDto
{
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? PhoneNumber { get; set; }
    [MaxLength(1000)] public string? Address { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    [Required, MaxLength(20)] public string PreferredLanguage { get; set; } = "bn-BD";
}

public class TenantMembershipDto
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public MembershipStatus Status { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public IReadOnlyList<RoleSummaryDto> Roles { get; set; } = Array.Empty<RoleSummaryDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateTenantMembershipRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long UserId { get; set; }
    public bool IsOwner { get; set; }
    public IReadOnlyList<long> RoleIds { get; set; } = Array.Empty<long>();
}

public class UpdateTenantMembershipStatusRequestDto
{
    public MembershipStatus Status { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class SetMembershipRolesRequestDto
{
    public IReadOnlyList<long> RoleIds { get; set; } = Array.Empty<long>();
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class RoleSummaryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; }
}

public class PermissionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class SaveRolePermissionsRequestDto
{
    public long RoleId { get; set; }
    public IReadOnlyList<RolePermissionSelectionDto> Permissions { get; set; } = Array.Empty<RolePermissionSelectionDto>();
}

public class RolePermissionSelectionDto
{
    public long PermissionId { get; set; }
    public bool IsAllowed { get; set; }
}

public class LoginHistoryDto
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public long? TenantId { get; set; }
    public DateTime OccurredAt { get; set; }
    public bool IsSuccess { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? FailureReason { get; set; }
}

public class ActiveSessionDto
{
    public long RefreshTokenId { get; set; }
    public string? DeviceId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class RevokeSessionRequestDto
{
    public long RefreshTokenId { get; set; }
}

public class TwoFactorStatusDto
{
    public bool IsEnabled { get; set; }
    public DateTime? EnabledAt { get; set; }
}
