using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class LessonPlan : BaseTenantEntity
    {
        public Guid? ClientRequestId { get; set; }

        [MaxLength(64)]
        public string? NaturalKey { get; set; }
        public long? ClassId { get; set; }
        public long SubjectId { get; set; }
        public long TeacherId { get; set; }

        public long? InstructorAssignmentId { get; set; }
        public long? AcademicBatchId { get; set; }
        public long? AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        [MaxLength(500)]
        public string ChapterName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Topic { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        [MaxLength(2000)]
        public string? Description { get; set; }

        [MaxLength(2000)]
        public string? LearningObjectives { get; set; }

        [MaxLength(2000)]
        public string? Resources { get; set; }

        [MaxLength(2000)]
        public string? ProgressNotes { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = LessonPlanStatus.Draft.ToString();

        public int ProgressPercent { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public long? SubmittedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public long? ReviewedBy { get; set; }

        [MaxLength(1000)]
        public string? ReviewRemarks { get; set; }

        public DateTime? CompletedAt { get; set; }
        public bool IsActive { get; set; } = true;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public virtual Class? Class { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual Employee? Teacher { get; set; }
        public virtual InstructorAssignment? InstructorAssignment { get; set; }
        public virtual AcademicBatch? AcademicBatch { get; set; }
        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual AcademicTerm? AcademicTerm { get; set; }
    }

    public enum LessonPlanStatus
    {
        Unknown = 0,
        Draft = 1,
        Submitted = 2,
        Approved = 3,
        InProgress = 4,
        Completed = 5,
        Rejected = 6
    }
}
