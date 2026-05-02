using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth;

public class RolePagePermission : BaseTenantEntity
{
    public long RoleId { get; set; }
    public ApplicationRole? Role { get; set; }

    public long AppPageId { get; set; }
    public AppPage? AppPage { get; set; } = null;   
    public long PermissionId { get; set; }
    public Permission? Permission { get; set; } = null;

    public bool IsAllowed { get; set; } = true;
}