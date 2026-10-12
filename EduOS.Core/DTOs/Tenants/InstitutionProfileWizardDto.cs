namespace EduOS.Core.DTOs.Tenants;

/// <summary>Legacy onboarding form contract; authoritative institution fields live in Tenant.</summary>
public sealed class InstitutionProfileWizardDto
{
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? OwnerEmail { get; set; }
    public string? OwnerPhone { get; set; }
    public string? OwnerDesignation { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

// Campus code is optional in the onboarding wizard only. The API generates it
// before passing a fully validated SaveCampusRequestDto to the foundation service.
public sealed class InstitutionCampusWizardRequestDto
{
    public long? Id { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required, global::System.ComponentModel.DataAnnotations.MaxLength(150)]
    public string Name { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? Code { get; set; }
    [global::System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Address { get; set; }
    [global::System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? Phone { get; set; }
    [global::System.ComponentModel.DataAnnotations.EmailAddress, global::System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? Email { get; set; }
    [global::System.ComponentModel.DataAnnotations.MaxLength(150)]
    public string? HeadName { get; set; }
    public bool IsHeadOffice { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

// These wizard contracts deliberately omit the required canonical Code field.
// The onboarding controller allocates codes server-side before calling Foundation.
public sealed class InstitutionAcademicYearWizardRequestDto
{
    public long? Id { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required, global::System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? Code { get; set; }
    public long? CampusId { get; set; }
    public EduOS.Core.Enums.Domain.AcademicCycleType CycleType { get; set; } = EduOS.Core.Enums.Domain.AcademicCycleType.Annual;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class InstitutionAcademicTermWizardRequestDto
{
    public long? Id { get; set; }
    public long AcademicYearId { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required, global::System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? Code { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}
