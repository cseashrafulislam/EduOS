namespace EduOS.Core.DTOs.Academic;

/// <summary>Onboarding may allocate an academic-year code on the server.</summary>
public sealed class AcademicYearOnboardingRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string? Code { get; set; }
    public long? CampusId { get; set; }
    public EduOS.Core.Enums.Domain.AcademicCycleType CycleType { get; set; } = EduOS.Core.Enums.Domain.AcademicCycleType.Annual;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class AcademicTermOnboardingRequestDto
{
    public long? Id { get; set; }
    public long AcademicYearId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string? Code { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}
