using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Learners;

/// <summary>
/// Append-only security record for every accepted learner identity attempt.
/// No identifier, digest, name, date of birth, or academic data is stored here.
/// </summary>
public class LearnerIdentityAccessLog : BaseTenantEntity
{
    public long? PersonId { get; set; }
    public long? StudentId { get; set; }
    public long? ConsentRequestId { get; set; }
    public long UserId { get; set; }
    public LearnerIdentityAccessAction Action { get; set; }
    public LearnerIdentityAccessOutcome Outcome { get; set; }
    public LearnerIdentityPurpose? Purpose { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public virtual Person? Person { get; set; }
    public virtual LearnerConsentRequest? ConsentRequest { get; set; }
}
