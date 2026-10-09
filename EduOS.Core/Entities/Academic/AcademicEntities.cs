using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic;

public class AcademicYear : BaseTenantEntity
{
    public long? CampusId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public AcademicCycleType CycleType { get; set; } = AcademicCycleType.Annual;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AcademicTerm : BaseTenantEntity
{
    public long AcademicYearId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(30)] public string? Code { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class AcademicDepartment : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AcademicProgram : BaseTenantEntity
{
    public long? AcademicDepartmentId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(100)] public string? ShortName { get; set; }
    public int DurationInMonths { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? TotalRequiredCredits { get; set; }
    public bool UsesCreditSystem { get; set; }
    [MaxLength(150)] public string? AwardTitle { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsAdmissionOpen { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class ProgramCampus : BaseTenantEntity
{
    public long AcademicProgramId { get; set; }
    public long CampusId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AcademicLevel : BaseTenantEntity
{
    public long AcademicProgramId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public int LevelNo { get; set; }
    public bool IsPromotable { get; set; } = true;
    public bool IsTerminalLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class AcademicTrack : BaseTenantEntity
{
    public long? AcademicProgramId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class Medium : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class Shift : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class AcademicBatch : BaseTenantEntity
{
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long AcademicProgramId { get; set; }
    public long AcademicLevelId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    public long? ShiftId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public DeliveryModeType DeliveryMode { get; set; } = DeliveryModeType.OnCampus;
    public int Capacity { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class Room : BaseTenantEntity
{
    public long CampusId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public int Capacity { get; set; }
    [MaxLength(100)] public string? RoomType { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Subject : BaseTenantEntity
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(50)] public string? ShortName { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DefaultCreditHours { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AcademicCurriculum : BaseTenantEntity
{
    public long AcademicProgramId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public int VersionNo { get; set; } = 1;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CurriculumSubject : BaseTenantEntity
{
    public long AcademicCurriculumId { get; set; }
    public long AcademicLevelId { get; set; }
    public long SubjectId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal FullMarks { get; set; } = 100;
    [Column(TypeName = "decimal(18,2)")] public decimal PassMarks { get; set; } = 33;
    [Column(TypeName = "decimal(18,2)")] public decimal CreditHours { get; set; }
    public bool IsOptional { get; set; }
    public bool HasPractical { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class SubjectPrerequisite : BaseTenantEntity
{
    public long AcademicCurriculumId { get; set; }
    public long SubjectId { get; set; }
    public long PrerequisiteSubjectId { get; set; }
    public bool IsMandatory { get; set; } = true;
}

public class SubjectOffering : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long AcademicBatchId { get; set; }
    public long CurriculumSubjectId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public DeliveryModeType DeliveryMode { get; set; } = DeliveryModeType.OnCampus;
    public bool IsActive { get; set; } = true;
}

public class StudentEnrollment : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long AcademicProgramId { get; set; }
    public long AcademicLevelId { get; set; }
    public long AcademicBatchId { get; set; }
    public long AcademicCurriculumId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    public long? ShiftId { get; set; }
    [Required, MaxLength(50)] public string RollNo { get; set; } = string.Empty;
    [MaxLength(50)] public string? RegistrationNo { get; set; }
    public DateOnly EnrollmentDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public EnrollmentState State { get; set; } = EnrollmentState.Active;
    public bool IsCurrent { get; set; } = true;
    [MaxLength(500)] public string? Remarks { get; set; }
}

public class StudentSubjectRegistration : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long SubjectOfferingId { get; set; }
    public SubjectRegistrationState State { get; set; } = SubjectRegistrationState.Approved;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "decimal(18,2)")] public decimal? CreditHoursSnapshot { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}

public class InstructorAssignment : BaseTenantEntity
{
    public long SubjectOfferingId { get; set; }
    public long EmployeeId { get; set; }
    public bool IsPrimary { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RoutineTimeSlot : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsBreak { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class RoutineEntry : BaseTenantEntity
{
    public long SubjectOfferingId { get; set; }
    public long RoutineTimeSlotId { get; set; }
    public long? InstructorAssignmentId { get; set; }
    public long? RoomId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Substitution : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long RoutineEntryId { get; set; }
    public DateOnly Date { get; set; }
    public long SubstituteEmployeeId { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    public bool IsCancelled { get; set; }
}

public class LessonPlan : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long SubjectOfferingId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly LessonDate { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Objectives { get; set; }
    [MaxLength(4000)] public string? Content { get; set; }
    [MaxLength(2000)] public string? Resources { get; set; }
    public LessonPlanState State { get; set; } = LessonPlanState.Draft;
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class AcademicCalendarPolicy : BaseTenantEntity
{
    public long? CampusId { get; set; }
    public DayOfWeek WeekendDay1 { get; set; } = DayOfWeek.Friday;
    public DayOfWeek? WeekendDay2 { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AcademicCalendarEvent : BaseTenantEntity
{
    public long? CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public CalendarEventKind EventType { get; set; } = CalendarEventKind.Other;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsPublicVisible { get; set; } = true;
}

public class AttendanceSession : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long AcademicBatchId { get; set; }
    public long? SubjectOfferingId { get; set; }
    public long? RoutineEntryId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public TimeOnly? StartsAt { get; set; }
    public TimeOnly? EndsAt { get; set; }
    public long? TakenByEmployeeId { get; set; }
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public long? FinalizedByUserId { get; set; }
}
