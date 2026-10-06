using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Learners;
using Microsoft.AspNetCore.Identity;

namespace EduOS.Core.Entities.Auth;

public class ApplicationUser : IdentityUser<long>
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long? PersonId { get; set; }
    [MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    [MaxLength(20)] public string PreferredLanguage { get; set; } = "bn-BD";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    [MaxLength(100)] public string? LastLoginIp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public virtual Person? Person { get; set; }
}

public class ApplicationRole : IdentityRole<long>
{
    public long? TenantId { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class TenantMembership : BaseTenantEntity
{
    public long UserId { get; set; }
    public bool IsOwner { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public long? DefaultCampusId { get; set; }
    public long? LastSelectedCampusId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }
}

public class Permission : BaseEntity
{
    [Required, MaxLength(150)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string ModuleCode { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RolePermission : BaseEntity
{
    public long RoleId { get; set; }
    public long PermissionId { get; set; }
    public bool IsAllowed { get; set; } = true;
}

public class LoginHistory : BaseEntity
{
    public long? UserId { get; set; }
    public long? TenantId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    [MaxLength(100)] public string? IpAddress { get; set; }
    [MaxLength(500)] public string? UserAgent { get; set; }
    [MaxLength(500)] public string? FailureReason { get; set; }
}

public class RefreshToken : BaseEntity
{
    public long UserId { get; set; }
    [Required, MaxLength(128)] public string TokenHash { get; set; } = string.Empty;
    [MaxLength(100)] public string? DeviceId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    [MaxLength(128)] public string? ReplacedByTokenHash { get; set; }
}

public class TwoFactorAuth : BaseEntity
{
    public long UserId { get; set; }
    [Required, MaxLength(2000)] public string ProtectedSecret { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
}

public class UserPermission : BaseEntity
{
    public long UserId { get; set; }
    public long? TenantId { get; set; }
    public long PermissionId { get; set; }
    public bool IsAllowed { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class UserCampusAccess : BaseTenantEntity
{
    public long UserId { get; set; }
    public long CampusId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TenantInvitation : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(200)] public string Email { get; set; } = string.Empty;
    public long? RoleId { get; set; }
    public long? CampusId { get; set; }
    [Required, MaxLength(128)] public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long InvitedByUserId { get; set; }
}

public class TwoFactorRecoveryCode : BaseEntity
{
    public long UserId { get; set; }
    [Required, MaxLength(128)] public string CodeHash { get; set; } = string.Empty;
    public DateTime? UsedAt { get; set; }
}
