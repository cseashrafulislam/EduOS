namespace EduOS.Core.DTOs.Learners;

public class PersonDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public IReadOnlyList<PersonIdentifierDto> Identifiers { get; set; } = Array.Empty<PersonIdentifierDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class UpdatePersonRequestDto
{
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(200)] public string? FullNameBangla { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(30)] public string? Gender { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [EmailAddress, MaxLength(200)] public string? Email { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class PersonIdentifierDto
{
    public long Id { get; set; }
    public PersonIdentifierKind IdentifierType { get; set; }
    public string MaskedValue { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class AddPersonIdentifierRequestDto
{
    public PersonIdentifierKind IdentifierType { get; set; }
    [Required, MaxLength(500)] public string IdentifierValue { get; set; } = string.Empty;
    [MaxLength(100)] public string? Issuer { get; set; }
}

public class VerifyPersonIdentifierRequestDto
{
    public long PersonIdentifierId { get; set; }
    public bool Verified { get; set; }
}

public class StudentPersonLinkDto
{
    public long Id { get; set; }
    public long PersonId { get; set; }
    public long StudentId { get; set; }
    public Guid StudentReference { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime LinkedAt { get; set; }
}

public class LinkStudentPersonRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long PersonId { get; set; }
    public Guid StudentReference { get; set; }
}

public class LearnerConsentRequestDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long PersonId { get; set; }
    public long RequestedStudentId { get; set; }
    public long RequestedByUserId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public ConsentState State { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class CreateLearnerConsentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid PersonReference { get; set; }
    public Guid StudentReference { get; set; }
    [Required, MaxLength(500)] public string Purpose { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}

public class ResolveLearnerConsentRequestDto
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class LearnerDataGrantDto
{
    public long Id { get; set; }
    public long LearnerConsentRequestId { get; set; }
    public long PersonId { get; set; }
    public long StudentId { get; set; }
    public long GrantedToUserId { get; set; }
    public GrantState State { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class LearnerIdentityAccessLogDto
{
    public long Id { get; set; }
    public long PersonId { get; set; }
    public long StudentId { get; set; }
    public long UserId { get; set; }
    public long? LearnerConsentRequestId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public DateTime AccessedAt { get; set; }
    public string? IpAddress { get; set; }
}
