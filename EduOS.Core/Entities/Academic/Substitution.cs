using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class Substitution : BaseTenantEntity
    {
        public Guid? ClientRequestId { get; set; }
        public DateTime Date { get; set; }
        public long OriginalTeacherId { get; set; }
        public long SubstituteTeacherId { get; set; }
        public long? ClassId { get; set; }
        public long SubjectId { get; set; }
        public long? RoutineEntryId { get; set; }
        public long? AcademicBatchId { get; set; }
        public long? RoutineTimeSlotId { get; set; }
        public long? AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }
        public string? Period { get; set; }

        [MaxLength(1000)]
        public string? Reason { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? CancelledAt { get; set; }
        public long? CancelledBy { get; set; }

        [MaxLength(1000)]
        public string? CancellationReason { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public virtual Employee? OriginalTeacher { get; set; }
        public virtual Employee? SubstituteTeacher { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual RoutineEntry? RoutineEntry { get; set; }
        public virtual AcademicBatch? AcademicBatch { get; set; }
        public virtual RoutineTimeSlot? RoutineTimeSlot { get; set; }
        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual AcademicTerm? AcademicTerm { get; set; }
    }
}
