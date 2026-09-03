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
