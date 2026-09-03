using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Learners;

/// <summary>
/// A tenant-scoped request for a learner/authorized guardian decision. Creating a
/// request grants no data access by itself.
/// </summary>
public class LearnerConsentRequest : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long PersonId { get; set; }
    public long RequestedStudentId { get; set; }
    public long RequestedByUserId { get; set; }
    public LearnerIdentityPurpose Purpose { get; set; }
    public LearnerDataScope RequestedScopes { get; set; }
    public LearnerConsentRequestStatus Status { get; set; } = LearnerConsentRequestStatus.Pending;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public long? ResolvedByUserId { get; set; }

    public virtual Person? Person { get; set; }
    public virtual Student? RequestedStudent { get; set; }
}
