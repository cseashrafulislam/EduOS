using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Learners;

public class Person : BaseEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? Gender { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(20)] public string PreferredLanguage { get; set; } = "bn-BD";
    [MaxLength(500)] public string? PhotoUrl { get; set; }
}

public class PersonIdentifier : BaseEntity
{
    public long PersonId { get; set; }
    public PersonIdentifierKind IdentifierType { get; set; }
    [Required, MaxLength(128)] public string LookupDigest { get; set; } = string.Empty;
    [Required, MaxLength(4000)] public string ProtectedValue { get; set; } = string.Empty;
    [MaxLength(100)] public string? Issuer { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class StudentPersonLink : BaseTenantEntity
{
    public long PersonId { get; set; }
    public long StudentId { get; set; }
    public bool IsPrimary { get; set; } = true;
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UnlinkedAt { get; set; }
    public long? LinkedByUserId { get; set; }
    public long? UnlinkedByUserId { get; set; }
    [MaxLength(500)] public string? LinkReason { get; set; }
}

public class LearnerConsentRequest : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long PersonId { get; set; }
    public long RequestedStudentId { get; set; }
    public long RequestedByUserId { get; set; }
    [Required, MaxLength(500)] public string Purpose { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string RequestedScopes { get; set; } = string.Empty;
    public ConsentState State { get; set; } = ConsentState.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public long? ResolvedByUserId { get; set; }
}

public class LearnerDataGrant : BaseTenantEntity
{
    public long LearnerConsentRequestId { get; set; }
    public long PersonId { get; set; }
    public long StudentId { get; set; }
    public long GrantedToUserId { get; set; }
    [Required, MaxLength(1000)] public string GrantedScopes { get; set; } = string.Empty;
    public GrantState State { get; set; } = GrantState.Active;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class LearnerIdentityAccessLog : BaseTenantEntity
{
    public long PersonId { get; set; }
    public long StudentId { get; set; }
    public long UserId { get; set; }
    public long? LearnerConsentRequestId { get; set; }
    [Required, MaxLength(100)] public string Action { get; set; } = string.Empty;
    [MaxLength(500)] public string? Purpose { get; set; }
    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string? IpAddress { get; set; }
}

public class PersonAddress : BaseEntity
{
    public long PersonId { get; set; }
    [Required, MaxLength(30)] public string AddressTypeCode { get; set; } = "Current";
    [MaxLength(500)] public string? AddressLine1 { get; set; }
    [MaxLength(500)] public string? AddressLine2 { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? District { get; set; }
    [MaxLength(100)] public string? DivisionOrState { get; set; }
    [MaxLength(30)] public string? PostalCode { get; set; }
    [MaxLength(10)] public string CountryCode { get; set; } = "BD";
    public bool IsPrimary { get; set; }
}

public class PersonMergeRecord : BaseEntity
{
    public long SourcePersonId { get; set; }
    public long TargetPersonId { get; set; }
    public long MergedByUserId { get; set; }
    public DateTime MergedAt { get; set; } = DateTime.UtcNow;
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
}
