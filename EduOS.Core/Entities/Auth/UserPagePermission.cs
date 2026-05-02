using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Auth;

public class UserPagePermission : BaseTenantEntity
{
    public long UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public long AppPageId { get; set; }
    public AppPage? AppPage { get; set; }

    public long PermissionId { get; set; }
    public Permission? Permission { get; set; }

    public bool IsAllowed { get; set; } = true;
}