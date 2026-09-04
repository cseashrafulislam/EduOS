using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class RoutineEntry : BaseTenantEntity
    {
        public long AcademicBatchId { get; set; }
        public long RoutineTimeSlotId { get; set; }

        public DayOfWeek DayOfWeek { get; set; }

        public long SubjectId { get; set; }
        public long EmployeeId { get; set; }

        public long? RoomId { get; set; }

        public long AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(500)]
        public string? Remarks { get; set; }

        public virtual AcademicBatch? AcademicBatch { get; set; }
        public virtual RoutineTimeSlot? RoutineTimeSlot { get; set; }
        public virtual Subject? Subject { get; set; }
    }
}