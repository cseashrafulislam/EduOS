using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Auth;

public class UserPagePermission : BaseAuditableEntity
{
    public long? TenantId { get; set; }
    public int UserId { get; set; }
    public ApplicationUser User { get; set; }

    public int AppPageId { get; set; }
    public AppPage AppPage { get; set; }

    public int PermissionId { get; set; }
    public Permission Permission { get; set; }

    public bool IsAllowed { get; set; } = true;
}