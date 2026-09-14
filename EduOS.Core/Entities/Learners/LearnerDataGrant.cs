using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Learners;

/// <summary>
/// A tenant's time-bound permission to use only the learner data scopes that
/// were approved for one stated purpose. The grant never transfers ownership
/// of another institution's records.
/// </summary>
public class LearnerDataGrant : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long PersonId { get; set; }
    public long StudentId { get; set; }
    public long ConsentRequestId { get; set; }
    public LearnerIdentityPurpose Purpose { get; set; }
    public LearnerDataScope GrantedScopes { get; set; }
    public LearnerDataGrantStatus Status { get; set; } = LearnerDataGrantStatus.Active;
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long GrantedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? RevokedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual Person? Person { get; set; }
    public virtual Student? Student { get; set; }
    public virtual LearnerConsentRequest? ConsentRequest { get; set; }
}
