using EduOS.Core.Entities.Base;
using EduOS.Core.Enums.Academics;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduOS.Core.Entities.Academics
{
    public class Medium : BaseTenantEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // Bangla / English / Arabic

        [Required, MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public class Shift : BaseTenantEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // Morning / Day / Evening

        [Required, MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }

        public bool IsDefault { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public class AcademicProgram : BaseTenantEntity
    {
        public long? CampusId { get; set; }
        public long? DepartmentId { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty; // School General / BBA / IELTS / Graphic Design

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? ShortName { get; set; }

        public int DurationInMonths { get; set; } = 0;

        [MaxLength(100)]
        public string? AwardTitle { get; set; } // SSC / HSC / BBA / Certificate

        public bool IsAdmissionOpen { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;

        [MaxLength(500)]
        public string? Description { get; set; }
    }

    public class AcademicLevel : BaseTenantEntity
    {
        public long AcademicProgramId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty; // Class 1 / 1st Year / Semester 1 / Module 1

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public int LevelNo { get; set; } = 1;

        public bool IsPromotable { get; set; } = true;
        public bool IsTerminalLevel { get; set; } = false;

        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public class AcademicTrack : BaseTenantEntity
    {
        public long? AcademicProgramId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty; // Science / Commerce / Arts / Major / Version

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? Description { get; set; }

        public bool IsDefault { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public class Subject : BaseTenantEntity
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? ShortName { get; set; }

        public SubjectType SubjectType { get; set; } = SubjectType.Core;

        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditHours { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal FullMarks { get; set; } = 100;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PassMarks { get; set; } = 33;

        public bool IsOptional { get; set; } = false;
        public bool HasPractical { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }



    public class AcademicBatch : BaseTenantEntity
    {
        public long CampusId { get; set; }
        public long AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public long AcademicProgramId { get; set; }
        public long AcademicLevelId { get; set; }

        public long? MediumId { get; set; }
        public long? ShiftId { get; set; }
        public long? AcademicTrackId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty; // Section A / BBA-61 / Weekend Batch

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.OnCampus;

        public int Capacity { get; set; } = 0;

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public bool IsDefault { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;

        [MaxLength(500)]
        public string? Remarks { get; set; }
    }

    public class CurriculumSubject : BaseTenantEntity
    {
        public long AcademicProgramId { get; set; }
        public long AcademicLevelId { get; set; }
        public long? AcademicTermId { get; set; }
        public long SubjectId { get; set; }

        public long? MediumId { get; set; }
        public long? AcademicTrackId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FullMarks { get; set; } = 100;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PassMarks { get; set; } = 33;

        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditHours { get; set; } = 0;

        public bool IsOptional { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public class StudentSubjectRegistration : BaseTenantEntity
    {
        public long StudentId { get; set; }
        public long AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }
        public long AcademicBatchId { get; set; }
        public long SubjectId { get; set; }

        public bool IsCurrent { get; set; } = true;
        public bool IsApproved { get; set; } = true;
        public DateTime RegistrationDate { get; set; } = DateTime.UtcNow.Date;

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }

    public class AcademicCalendarEvent : BaseTenantEntity
    {
        public long? CampusId { get; set; }
        public long AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public CalendarEventType EventType { get; set; } = CalendarEventType.Other;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsHoliday { get; set; } = false;
        public bool IsPublicVisible { get; set; } = true;
    }

    public class Student : BaseTenantEntity
    {
        [Required, MaxLength(50)]
        public string StudentCode { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? FullNameBangla { get; set; }

        public GenderType Gender { get; set; } = GenderType.Male;
        public DateTime? DateOfBirth { get; set; }

        [MaxLength(20)]
        public string? Mobile { get; set; }

        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(20)]
        public string? BloodGroup { get; set; }

        [MaxLength(100)]
        public string? Religion { get; set; }

        [MaxLength(100)]
        public string? Nationality { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        [MaxLength(500)]
        public string? PhotoUrl { get; set; }

        public DateTime AdmissionDate { get; set; } = DateTime.UtcNow.Date;

        public bool IsActive { get; set; } = true;

        [MaxLength(50)]
        public string? Status { get; set; } // Active / Passed / Dropout / Suspended
    }

    public class Guardian : BaseTenantEntity
    {
        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Mobile { get; set; }

        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(150)]
        public string? Occupation { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        public GenderType? Gender { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class StudentGuardianLink : BaseTenantEntity
    {
        public long StudentId { get; set; }
        public long GuardianId { get; set; }

        public GuardianRelationType RelationType { get; set; } = GuardianRelationType.Other;
        public bool IsPrimary { get; set; } = false;
        public bool CanReceiveSms { get; set; } = true;
        public bool CanReceiveEmail { get; set; } = true;
    }

    public class Instructor : BaseTenantEntity
    {
        public int? UserId { get; set; } // AspNetUsers link হলে

        [Required, MaxLength(50)]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        public GenderType Gender { get; set; } = GenderType.Male;

        [MaxLength(20)]
        public string? Mobile { get; set; }

        [MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(100)]
        public string? Designation { get; set; } // Teacher / Trainer / Lecturer / Professor

        [MaxLength(150)]
        public string? DepartmentName { get; set; }

        [MaxLength(300)]
        public string? Qualification { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        [MaxLength(500)]
        public string? PhotoUrl { get; set; }

        public DateTime? JoiningDate { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class StudentEnrollment : BaseTenantEntity
    {
        public long StudentId { get; set; }

        public long CampusId { get; set; }
        public long AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public long AcademicProgramId { get; set; }
        public long AcademicLevelId { get; set; }
        public long AcademicBatchId { get; set; }

        public long? MediumId { get; set; }
        public long? ShiftId { get; set; }
        public long? AcademicTrackId { get; set; }

        [Required, MaxLength(50)]
        public string RollNo { get; set; } = string.Empty;

        public DateTime EnrollmentDate { get; set; } = DateTime.UtcNow.Date;

        public EnrollmentStatus EnrollmentStatus { get; set; } = EnrollmentStatus.Active;
        public bool IsCurrent { get; set; } = true;
        public bool IsActive { get; set; } = true;

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }

    public class PromotionRecord : BaseTenantEntity
    {
        public long StudentId { get; set; }
        public long FromEnrollmentId { get; set; }
        public long ToEnrollmentId { get; set; }

        public DateTime PromotionDate { get; set; } = DateTime.UtcNow.Date;

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }

    public class InstructorAssignment : BaseTenantEntity
    {
        public long AcademicBatchId { get; set; }
        public long SubjectId { get; set; }
        public long InstructorId { get; set; }

        public long? AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public bool IsPrimary { get; set; } = true;
        public bool IsActive { get; set; } = true;
    }

    public class RoutineTimeSlot : BaseTenantEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // Period 1 / Slot A

        public int DayOfWeek { get; set; } // 1-7

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public bool IsBreak { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public class RoutineEntry : BaseTenantEntity
    {
        public long AcademicBatchId { get; set; }
        public long RoutineTimeSlotId { get; set; }

        public long SubjectId { get; set; }
        public long InstructorId { get; set; }

        public long? RoomId { get; set; }
        public long? AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }


    public class AttendanceSession : BaseTenantEntity
    {
        public long AcademicBatchId { get; set; }
        public long? SubjectId { get; set; }
        public long? InstructorId { get; set; }
        public long? RoutineTimeSlotId { get; set; }

        public DateTime AttendanceDate { get; set; }

        public bool IsFinalized { get; set; } = false;

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }

    public class StudentAttendance : BaseTenantEntity
    {
        public long AttendanceSessionId { get; set; }
        public long StudentId { get; set; }

        public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
        public TimeSpan? CheckInTime { get; set; }

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }

    public class EmployeeAttendance : BaseTenantEntity
    {
        public long InstructorId { get; set; }
        public DateTime AttendanceDate { get; set; }

        public EmployeeAttendanceStatus Status { get; set; } = EmployeeAttendanceStatus.Present;

        public TimeSpan? CheckInTime { get; set; }
        public TimeSpan? CheckOutTime { get; set; }

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }


    public class Assessment : BaseTenantEntity
    {
        public long CampusId { get; set; }
        public long AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty; // Mid Term / Final / Quiz 1

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public AssessmentType AssessmentType { get; set; } = AssessmentType.Exam;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsPublished { get; set; } = false;
        public bool IsActive { get; set; } = true;

        [MaxLength(500)]
        public string? Remarks { get; set; }
    }

    public class AssessmentSubject : BaseTenantEntity
    {
        public long AssessmentId { get; set; }
        public long AcademicBatchId { get; set; }
        public long SubjectId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FullMarks { get; set; } = 100;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PassMarks { get; set; } = 33;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Weightage { get; set; } = 100;

        public bool IsOptional { get; set; } = false;
    }

    public class AssessmentSchedule : BaseTenantEntity
    {
        public long AssessmentId { get; set; }
        public long AcademicBatchId { get; set; }
        public long SubjectId { get; set; }

        public DateTime ExamDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }

        public long? RoomId { get; set; }

        [MaxLength(200)]
        public string? InvigilatorName { get; set; }

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }

    public class StudentAssessmentMark : BaseTenantEntity
    {
        public long AssessmentId { get; set; }
        public long AcademicBatchId { get; set; }
        public long StudentId { get; set; }
        public long SubjectId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ObtainedMarks { get; set; } = 0;

        public bool IsAbsent { get; set; } = false;
        public bool IsWithheld { get; set; } = false;

        [MaxLength(10)]
        public string? GradeLetter { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? GradePoint { get; set; }

        [MaxLength(300)]
        public string? Remarks { get; set; }
    }

    public class GradeRule : BaseTenantEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty; // Default GPA Rule

        [Column(TypeName = "decimal(18,2)")]
        public decimal MinMarks { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MaxMarks { get; set; }

        [Required, MaxLength(10)]
        public string GradeLetter { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal GradePoint { get; set; }

        public bool IsFailGrade { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }

    public class ResultPublish : BaseTenantEntity
    {
        public long AssessmentId { get; set; }
        public DateTime PublishAt { get; set; }

        public int? PublishedByUserId { get; set; }

        public bool VisibleToStudent { get; set; } = true;
        public bool VisibleToGuardian { get; set; } = true;
        public bool IsSmsSent { get; set; } = false;
        public bool IsEmailSent { get; set; } = false;

        [MaxLength(500)]
        public string? PublishNote { get; set; }
    }

    public class CertificateTemplate : BaseTenantEntity
    {
        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? HeaderText { get; set; }

        [MaxLength(500)]
        public string? FooterText { get; set; }

        [MaxLength(1000)]
        public string? HtmlTemplate { get; set; }

        public bool IsDefault { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }

}