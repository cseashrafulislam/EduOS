using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Learners;

/// <summary>
/// Tenant-owned link between an institution's private student record and the
/// platform-global person. It does not grant access to another tenant's records.
/// </summary>
public class StudentPersonLink : BaseTenantEntity
{
    public long StudentId { get; set; }
    public long PersonId { get; set; }
    public StudentPersonLinkStatus Status { get; set; } = StudentPersonLinkStatus.Active;
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public long LinkedByUserId { get; set; }

    public virtual Student? Student { get; set; }
    public virtual Person? Person { get; set; }
}
