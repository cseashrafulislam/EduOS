using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Learners;

/// <summary>
/// Protected government identifier. The encrypted value is recoverable only for
/// approved workflows; equality lookup uses a server-keyed HMAC digest.
/// </summary>
public class PersonIdentifier : BaseEntity
{
    public long PersonId { get; set; }
    public PersonIdentifierType Type { get; set; }
    public string ProtectedValue { get; set; } = string.Empty;
    public string LookupDigest { get; set; } = string.Empty;
    public IdentifierVerificationStatus VerificationStatus { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationProvider { get; set; }

    public virtual Person? Person { get; set; }
}
