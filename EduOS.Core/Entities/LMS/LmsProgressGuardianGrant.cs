using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS;

/// <summary>Explicit learner consent for one enrollment and one active guardian relationship.
/// Revocation is historical; neither certificate consent nor a family relationship implies progress access.</summary>
public sealed class LmsProgressGuardianGrant : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long CourseEnrollmentId { get; set; }
    public long StudentGuardianId { get; set; }
    public DateTime GrantedAt { get; set; }
    public long GrantedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? RevokedByUserId { get; set; }
}
