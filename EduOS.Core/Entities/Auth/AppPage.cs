using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Auth;

public class AppPage : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;       // Students
    public string Code { get; set; } = string.Empty;       // STUDENTS
    public string Url { get; set; } = string.Empty;        // /Students
    public string GroupName { get; set; } = string.Empty;  // Academic / Admin
    public string? Icon { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}