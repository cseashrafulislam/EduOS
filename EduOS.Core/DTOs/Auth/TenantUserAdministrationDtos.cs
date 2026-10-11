using EduOS.Core.Common;
using EduOS.Core.Enums.Domain;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Auth;

public sealed class TenantUserListItemDto
{
    public long MembershipId { get; set; }
    public long UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsOwner { get; set; }
    public MembershipStatus Status { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = "";
    public IReadOnlyList<long> RoleIds { get; set; } = Array.Empty<long>();
}

public sealed class TenantRoleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<long> PermissionIds { get; set; } = Array.Empty<long>();
}

public sealed class TenantPermissionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ModuleCode { get; set; } = "";
}

public sealed class TenantRoleCreateRequestDto
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = "";
    [StringLength(300)] public string? Description { get; set; }
}

public sealed class TenantRoleUpdateRequestDto
{
    [StringLength(300)] public string? Description { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class TenantRolePermissionsRequestDto
{
    [Required] public IReadOnlyList<long> PermissionIds { get; set; } = Array.Empty<long>();
}

public sealed class TenantMemberRolesRequestDto
{
    [Required] public string RowVersion { get; set; } = "";
    [Required] public IReadOnlyList<long> RoleIds { get; set; } = Array.Empty<long>();
}

public sealed class TenantMemberStatusRequestDto
{
    [Required] public string RowVersion { get; set; } = "";
    [Required] public MembershipStatus Status { get; set; }
}
