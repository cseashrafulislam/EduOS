using System.ComponentModel.DataAnnotations;
using EduOS.Core.Entities.Academic;

namespace EduOS.Core.DTOs.Academic;

public sealed class AcademicSetupCatalogDto
{
    public IReadOnlyList<AcademicProgramDto> Programs { get; set; } = [];
    public IReadOnlyList<AcademicLevelDto> Levels { get; set; } = [];
    public IReadOnlyList<AcademicSubjectDto> Subjects { get; set; } = [];
    public IReadOnlyList<AcademicCurriculumDto> Curricula { get; set; } = [];
    public IReadOnlyList<CurriculumSubjectDto> CurriculumSubjects { get; set; } = [];
    public IReadOnlyList<AcademicBatchDto> Batches { get; set; } = [];
    public IReadOnlyList<AcademicRoomDto> Rooms { get; set; } = [];
}

public sealed class AcademicProgramDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public long? DepartmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public int DurationInMonths { get; set; }
    public string? AwardTitle { get; set; }
    public bool IsAdmissionOpen { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateAcademicProgramDto
{
    public long? CampusId { get; set; }
    public long? DepartmentId { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    [StringLength(100)] public string? ShortName { get; set; }
    [Range(1, 600)] public int DurationInMonths { get; set; }
    [StringLength(150)] public string? AwardTitle { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
    public bool IsAdmissionOpen { get; set; } = true;
}

public sealed class AcademicLevelDto
{
    public long Id { get; set; }
    public long AcademicProgramId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int LevelNo { get; set; }
    public bool IsPromotable { get; set; }
    public bool IsTerminalLevel { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateAcademicLevelDto
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    [Range(1, 100)] public int LevelNo { get; set; } = 1;
    public bool IsPromotable { get; set; } = true;
    public bool IsTerminalLevel { get; set; }
}

public sealed class AcademicSubjectDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public SubjectType SubjectType { get; set; }
    public decimal DefaultCreditHours { get; set; }
    public decimal DefaultFullMarks { get; set; }
    public decimal DefaultPassMarks { get; set; }
    public bool HasPractical { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateAcademicSubjectDto
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    [StringLength(100)] public string? ShortName { get; set; }
    public SubjectType SubjectType { get; set; } = SubjectType.Core;
    [Range(typeof(decimal), "0", "1000")] public decimal DefaultCreditHours { get; set; }
    [Range(typeof(decimal), "0.01", "100000")] public decimal DefaultFullMarks { get; set; } = 100;
    [Range(typeof(decimal), "0", "100000")] public decimal DefaultPassMarks { get; set; } = 33;
    public bool HasPractical { get; set; }
}

public sealed class AcademicCurriculumDto
{
    public long Id { get; set; }
    public long AcademicProgramId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public long? EffectiveFromAcademicYearId { get; set; }
    public long? EffectiveToAcademicYearId { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateAcademicCurriculumDto
{
    [Range(1, long.MaxValue)] public long AcademicProgramId { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    public long? EffectiveFromAcademicYearId { get; set; }
    public long? EffectiveToAcademicYearId { get; set; }
    public bool IsCurrent { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
}

public sealed class CurriculumSubjectDto
{
    public long Id { get; set; }
    public long AcademicCurriculumId { get; set; }
    public long AcademicLevelId { get; set; }
    public long SubjectId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public decimal CreditHours { get; set; }
    public bool IsOptional { get; set; }
    public bool HasPractical { get; set; }
    public bool IsActive { get; set; }
}

public sealed class RegisterCurriculumSubjectDto
{
    [Range(1, long.MaxValue)] public long AcademicLevelId { get; set; }
    [Range(1, long.MaxValue)] public long SubjectId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    [Range(typeof(decimal), "0.01", "100000")] public decimal FullMarks { get; set; } = 100;
    [Range(typeof(decimal), "0", "100000")] public decimal PassMarks { get; set; } = 33;
    [Range(typeof(decimal), "0", "1000")] public decimal CreditHours { get; set; }
    public bool IsOptional { get; set; }
    public bool HasPractical { get; set; }
}

public sealed class AcademicBatchDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long AcademicProgramId { get; set; }
    public long AcademicLevelId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    public long? ShiftId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DeliveryMode DeliveryMode { get; set; }
    public int Capacity { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateAcademicBatchDto
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicProgramId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicLevelId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    public long? ShiftId { get; set; }
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.OnCampus;
    [Range(1, 100000)] public int Capacity { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
}

public sealed class AcademicRoomDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? BuildingName { get; set; }
    public string? Floor { get; set; }
    public int Capacity { get; set; }
    public bool IsLab { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CreateAcademicRoomDto
{
    public long? CampusId { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Code { get; set; } = string.Empty;
    [StringLength(100)] public string? BuildingName { get; set; }
    [StringLength(50)] public string? Floor { get; set; }
    [Range(1, 100000)] public int Capacity { get; set; }
    public bool IsLab { get; set; }
}
