using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS;

/// <summary>
/// Explicit, revocable learner authorization for one certificate and one tenant-local
/// guardian relationship. Historical grants remain as records after revocation.
/// The relationship must still be active at every read; a grant never outlives it.
/// </summary>
public sealed class LmsCertificateGuardianGrant : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long CertificateId { get; set; }
    public long StudentGuardianId { get; set; }
    public DateTime GrantedAt { get; set; }
    public long GrantedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public long? RevokedByUserId { get; set; }
}
