using EduOS.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Student;

public class RegisterLearnerIdentityRequestDto
{
    [Range(1, long.MaxValue)]
    public long StudentId { get; set; }

    [Required]
    [StringLength(32, MinimumLength = 10)]
    public string IdentifierValue { get; set; } = string.Empty;

    public PersonIdentifierType IdentifierType { get; set; }
    public LearnerIdentityPurpose? Purpose { get; set; }
    public LearnerDataScope RequestedScopes { get; set; } = LearnerDataScope.BasicIdentity;
}

public class LearnerIdentityResultDto
{
    public string State { get; set; } = string.Empty;
    public Guid? PersonReference { get; set; }
    public Guid? ConsentRequestReference { get; set; }
    public bool ConsentRequired { get; set; }
    public DateTime? ConsentRequestExpiresAt { get; set; }
}

public class ResolveLearnerConsentRequestDto
{
    public LearnerConsentDecision Decision { get; set; }
}

public class LearnerConsentRequestDto
{
    public Guid Reference { get; set; }
    public string RequestingInstitution { get; set; } = string.Empty;
    public LearnerIdentityPurpose Purpose { get; set; }
    public LearnerDataScope RequestedScopes { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class LearnerDataGrantDto
{
    public Guid Reference { get; set; }
    public string Institution { get; set; } = string.Empty;
    public LearnerIdentityPurpose Purpose { get; set; }
    public LearnerDataScope GrantedScopes { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class LearnerConsentResolutionDto
{
    public string State { get; set; } = string.Empty;
    public bool AlreadyProcessed { get; set; }
    public Guid? GrantReference { get; set; }
    public DateTime? GrantExpiresAt { get; set; }
}
