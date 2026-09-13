using EduOS.Core.Common;
using EduOS.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Admission;

public class CreateAdmissionApplicationDto
{
    public Guid ClientRequestId { get; set; }

    [Range(1, long.MaxValue)]
    public long AcademicYearId { get; set; }

    [Range(1, long.MaxValue)]
    public long? AcademicTermId { get; set; }

    [Range(1, long.MaxValue)]
    public long CampusId { get; set; }

    [Range(1, long.MaxValue)]
    public long AcademicUnitId { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    public string ApplicantName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? ApplicantNameBangla { get; set; }

    public DateTime DateOfBirth { get; set; }
    public Gender Gender { get; set; }

    [Required, StringLength(30, MinimumLength = 8)]
    public string PrimaryMobile { get; set; } = string.Empty;

    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [StringLength(200)]
    public string? GuardianName { get; set; }

    [StringLength(50)]
    public string? GuardianRelation { get; set; }

    [StringLength(30)]
    public string? GuardianMobile { get; set; }

    [StringLength(1000)]
    public string? PresentAddress { get; set; }

    [StringLength(1000)]
    public string? PermanentAddress { get; set; }

    [StringLength(200)]
    public string? PreviousInstitution { get; set; }

    [RegularExpression("^(en-BD|bn-BD)$")]
    public string PreferredLanguage { get; set; } = "bn-BD";
}

public class AdmissionApplicationQueryDto
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [StringLength(100)]
    public string? Search { get; set; }

    public AdmissionApplicationStatus? Status { get; set; }

    [Range(1, long.MaxValue)]
    public long? AcademicYearId { get; set; }

    [Range(1, long.MaxValue)]
    public long? CampusId { get; set; }

    [Range(1, long.MaxValue)]
    public long? AcademicUnitId { get; set; }
}

public class ReviewAdmissionApplicationDto
{
    public AdmissionApplicationStatus Status { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? DecisionNote { get; set; }
}

public class AdmissionApplicationCreatedDto
{
    public Guid Reference { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public AdmissionApplicationStatus Status { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AdmissionApplicationListItemDto
{
    public Guid Reference { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public string MaskedMobile { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long AcademicUnitId { get; set; }
    public string AcademicUnitName { get; set; } = string.Empty;
    public AdmissionApplicationStatus Status { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AdmissionApplicationDetailsDto : AdmissionApplicationListItemDto
{
    public string? ApplicantNameBangla { get; set; }
    public DateTime DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string PrimaryMobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianRelation { get; set; }
    public string? GuardianMobile { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? PreviousInstitution { get; set; }
    public string PreferredLanguage { get; set; } = string.Empty;
    public long? AcademicTermId { get; set; }
    public string? AcademicTermName { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
}

public class AdmissionReferenceOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? ParentId { get; set; }
}

public class AdmissionApplicationOptionsDto
{
    public List<AdmissionReferenceOptionDto> AcademicYears { get; set; } = new();
    public List<AdmissionReferenceOptionDto> AcademicTerms { get; set; } = new();
    public List<AdmissionReferenceOptionDto> Campuses { get; set; } = new();
    public List<AdmissionReferenceOptionDto> AcademicUnits { get; set; } = new();
}

public class AdmitAdmissionApplicationDto
{
    [Range(1, long.MaxValue)]
    public long SectionId { get; set; }

    [Range(1, long.MaxValue)]
    public long? GroupId { get; set; }

    [Required, StringLength(50, MinimumLength = 1)]
    public string Roll { get; set; } = string.Empty;

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}

public class AdmissionEnrollmentOptionsDto
{
    public List<AdmissionReferenceOptionDto> Sections { get; set; } = new();
    public List<AdmissionReferenceOptionDto> Groups { get; set; } = new();
}

public class AdmittedStudentDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Roll { get; set; } = string.Empty;
    public long StudentId { get; set; }
    public long EnrollmentId { get; set; }
    public AdmissionApplicationStatus ApplicationStatus { get; set; }
}
