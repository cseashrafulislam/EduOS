namespace EduOS.Core.DTOs.Academic;

public class AcademicYearDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public string? CampusName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public AcademicCycleType CycleType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicYearRequestDto
{
    public long? CampusId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public AcademicCycleType CycleType { get; set; } = AcademicCycleType.Annual;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class AcademicTermDto
{
    public long Id { get; set; }
    public long AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicTermRequestDto
{
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

public class AcademicDepartmentDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicDepartmentRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class AcademicProgramDto
{
    public long Id { get; set; }
    public long? AcademicDepartmentId { get; set; }
    public string? AcademicDepartmentName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public int DurationInMonths { get; set; }
    public string? AwardTitle { get; set; }
    public string? Description { get; set; }
    public bool IsAdmissionOpen { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyList<long> CampusIds { get; set; } = Array.Empty<long>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicProgramRequestDto
{
    public long? AcademicDepartmentId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(100)] public string? ShortName { get; set; }
    [Range(0, 1200)] public int DurationInMonths { get; set; }
    [MaxLength(150)] public string? AwardTitle { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsAdmissionOpen { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public IReadOnlyList<long> CampusIds { get; set; } = Array.Empty<long>();
    public string? RowVersion { get; set; }
}

public class ProgramCampusDto
{
    public long Id { get; set; }
    public long AcademicProgramId { get; set; }
    public string AcademicProgramName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveProgramCampusRequestDto
{
    public long AcademicProgramId { get; set; }
    public long CampusId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class AcademicLevelDto
{
    public long Id { get; set; }
    public long AcademicProgramId { get; set; }
    public string AcademicProgramName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int LevelNo { get; set; }
    public bool IsPromotable { get; set; }
    public bool IsTerminalLevel { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicLevelRequestDto
{
    public long AcademicProgramId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(1, 1000)] public int LevelNo { get; set; }
    public bool IsPromotable { get; set; } = true;
    public bool IsTerminalLevel { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class AcademicTrackDto
{
    public long Id { get; set; }
    public long? AcademicProgramId { get; set; }
    public string? AcademicProgramName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicTrackRequestDto
{
    public long? AcademicProgramId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class MediumDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveMediumRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class ShiftDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveShiftRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Code { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class AcademicBatchDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public long? AcademicTermId { get; set; }
    public string? AcademicTermName { get; set; }
    public long AcademicProgramId { get; set; }
    public string AcademicProgramName { get; set; } = string.Empty;
    public long AcademicLevelId { get; set; }
    public string AcademicLevelName { get; set; } = string.Empty;
    public long? AcademicTrackId { get; set; }
    public string? AcademicTrackName { get; set; }
    public long? MediumId { get; set; }
    public string? MediumName { get; set; }
    public long? ShiftId { get; set; }
    public string? ShiftName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DeliveryModeType DeliveryMode { get; set; }
    public int Capacity { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicBatchRequestDto
{
    public Guid ClientRequestId { get; set; }
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
    [Range(0, 100000)] public int Capacity { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class RoomDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string? RoomType { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveRoomRequestDto
{
    public long CampusId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(0, 100000)] public int Capacity { get; set; }
    [MaxLength(100)] public string? RoomType { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class SubjectDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public decimal DefaultCreditHours { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveSubjectRequestDto
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [MaxLength(50)] public string? ShortName { get; set; }
    [Range(typeof(decimal), "0", "1000")] public decimal DefaultCreditHours { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class AcademicCurriculumDto
{
    public long Id { get; set; }
    public long AcademicProgramId { get; set; }
    public string AcademicProgramName { get; set; } = string.Empty;
    public long? AcademicTrackId { get; set; }
    public string? AcademicTrackName { get; set; }
    public long? MediumId { get; set; }
    public string? MediumName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int VersionNo { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<CurriculumSubjectDto> Subjects { get; set; } = Array.Empty<CurriculumSubjectDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicCurriculumRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long AcademicProgramId { get; set; }
    public long? AcademicTrackId { get; set; }
    public long? MediumId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int VersionNo { get; set; } = 1;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<SaveCurriculumSubjectRequestDto> Subjects { get; set; } = Array.Empty<SaveCurriculumSubjectRequestDto>();
    public string? RowVersion { get; set; }
}

public class CurriculumSubjectDto
{
    public long Id { get; set; }
    public long AcademicCurriculumId { get; set; }
    public long AcademicLevelId { get; set; }
    public string AcademicLevelName { get; set; } = string.Empty;
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public decimal CreditHours { get; set; }
    public bool IsOptional { get; set; }
    public bool HasPractical { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveCurriculumSubjectRequestDto
{
    public long? Id { get; set; }
    public long AcademicLevelId { get; set; }
    public long SubjectId { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal FullMarks { get; set; } = 100;
    [Range(typeof(decimal), "0", "100000")] public decimal PassMarks { get; set; } = 33;
    [Range(typeof(decimal), "0", "1000")] public decimal CreditHours { get; set; }
    public bool IsOptional { get; set; }
    public bool HasPractical { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class SubjectPrerequisiteDto
{
    public long Id { get; set; }
    public long AcademicCurriculumId { get; set; }
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long PrerequisiteSubjectId { get; set; }
    public string PrerequisiteSubjectName { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveSubjectPrerequisiteRequestDto
{
    public long AcademicCurriculumId { get; set; }
    public long SubjectId { get; set; }
    public long PrerequisiteSubjectId { get; set; }
    public bool IsMandatory { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class SubjectOfferingDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public long CurriculumSubjectId { get; set; }
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public DeliveryModeType DeliveryMode { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<InstructorAssignmentDto> Instructors { get; set; } = Array.Empty<InstructorAssignmentDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveSubjectOfferingRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long AcademicBatchId { get; set; }
    public long CurriculumSubjectId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(0, 100000)] public int Capacity { get; set; }
    public DeliveryModeType DeliveryMode { get; set; } = DeliveryModeType.OnCampus;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class StudentEnrollmentDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;
    public long? AcademicTermId { get; set; }
    public string? AcademicTermName { get; set; }
    public long AcademicProgramId { get; set; }
    public string AcademicProgramName { get; set; } = string.Empty;
    public long AcademicLevelId { get; set; }
    public string AcademicLevelName { get; set; } = string.Empty;
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public long AcademicCurriculumId { get; set; }
    public string AcademicCurriculumName { get; set; } = string.Empty;
    public long? AcademicTrackId { get; set; }
    public string? AcademicTrackName { get; set; }
    public long? MediumId { get; set; }
    public string? MediumName { get; set; }
    public long? ShiftId { get; set; }
    public string? ShiftName { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public DateOnly EnrollmentDate { get; set; }
    public EnrollmentState State { get; set; }
    public bool IsCurrent { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateStudentEnrollmentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    /// <summary>Campus, academic year, program, level, track, medium and shift are resolved from the selected batch.</summary>
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    /// <summary>Selected curriculum must be validated against the selected batch and enrollment date.</summary>
    [Range(1, long.MaxValue)] public long AcademicCurriculumId { get; set; }
    [Required, MaxLength(50)] public string RollNo { get; set; } = string.Empty;
    public DateOnly EnrollmentDate { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}
public class CloseStudentEnrollmentRequestDto
{
    public EnrollmentState FinalState { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class StudentSubjectRegistrationDto
{
    public long Id { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long SubjectOfferingId { get; set; }
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public SubjectRegistrationState State { get; set; }
    public DateTime RegisteredAt { get; set; }
    public long? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class RegisterStudentSubjectRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid SubjectOfferingReference { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
}

public class ChangeSubjectRegistrationStateRequestDto
{
    public SubjectRegistrationState State { get; set; }
    [MaxLength(500)] public string? Remarks { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class InstructorAssignmentDto
{
    public long Id { get; set; }
    public long SubjectOfferingId { get; set; }
    public long EmployeeId { get; set; }
    public Guid EmployeeReference { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveInstructorAssignmentRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid SubjectOfferingReference { get; set; }
    public Guid EmployeeReference { get; set; }
    public bool IsPrimary { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class RoutineTimeSlotDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsBreak { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveRoutineTimeSlotRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsBreak { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public class RoutineEntryDto
{
    public long Id { get; set; }
    public long SubjectOfferingId { get; set; }
    public Guid SubjectOfferingReference { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long RoutineTimeSlotId { get; set; }
    public string TimeSlotName { get; set; } = string.Empty;
    public long? RoomId { get; set; }
    public string? RoomName { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveRoutineEntryRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid SubjectOfferingReference { get; set; }
    public long RoutineTimeSlotId { get; set; }
    public long? RoomId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class SubstitutionDto
{
    public long Id { get; set; }
    public long RoutineEntryId { get; set; }
    public DateOnly Date { get; set; }
    public long SubstituteEmployeeId { get; set; }
    public string SubstituteEmployeeName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool IsCancelled { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateSubstitutionRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long RoutineEntryId { get; set; }
    public DateOnly Date { get; set; }
    public Guid SubstituteEmployeeReference { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}

public class CancelSubstitutionRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class LessonPlanDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long SubjectOfferingId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public long EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly LessonDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Objectives { get; set; }
    public string? Content { get; set; }
    public string? Resources { get; set; }
    public LessonPlanState State { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveLessonPlanRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid SubjectOfferingReference { get; set; }
    public DateOnly LessonDate { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Objectives { get; set; }
    [MaxLength(4000)] public string? Content { get; set; }
    [MaxLength(2000)] public string? Resources { get; set; }
    public string? RowVersion { get; set; }
}

public class ReviewLessonPlanRequestDto
{
    public bool Approve { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public class AcademicCalendarPolicyDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public string? CampusName { get; set; }
    public DayOfWeek WeekendDay1 { get; set; }
    public DayOfWeek? WeekendDay2 { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicCalendarPolicyRequestDto
{
    public long? CampusId { get; set; }
    public DayOfWeek WeekendDay1 { get; set; } = DayOfWeek.Friday;
    public DayOfWeek? WeekendDay2 { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class AcademicCalendarEventDto
{
    public long Id { get; set; }
    public long? CampusId { get; set; }
    public string? CampusName { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public CalendarEventKind EventType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsPublicVisible { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveAcademicCalendarEventRequestDto
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
    public string? RowVersion { get; set; }
}

public class AttendanceSessionDto
{
    public long Id { get; set; }
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public long? SubjectOfferingId { get; set; }
    public string? SubjectName { get; set; }
    public long? RoutineEntryId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public TimeOnly? StartsAt { get; set; }
    public TimeOnly? EndsAt { get; set; }
    public bool IsFinalized { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateAttendanceSessionRequestDto
{
    public Guid ClientRequestId { get; set; }
    public long AcademicBatchId { get; set; }
    public Guid? SubjectOfferingReference { get; set; }
    public long? RoutineEntryId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public TimeOnly? StartsAt { get; set; }
    public TimeOnly? EndsAt { get; set; }
}

public class FinalizeAttendanceSessionRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
