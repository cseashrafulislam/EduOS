using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Auth;

public class Permission : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;   // View / Create / Edit / Delete
    public string Code { get; set; } = string.Empty;   // VIEW / CREATE / EDIT / DELETE
    public bool IsActive { get; set; } = true;
}