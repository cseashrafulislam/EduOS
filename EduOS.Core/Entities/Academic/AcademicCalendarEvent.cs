using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class AcademicCalendarEvent : BaseTenantEntity
    {
        public long? CampusId { get; set; }

        public long? AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public AcademicCalendarEventType EventType { get; set; } = AcademicCalendarEventType.Other;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        [MaxLength(300)]
        public string? Location { get; set; }

        public bool IsHoliday { get; set; }
        public bool IsPublicVisible { get; set; } = true;
        public bool IsActive { get; set; } = true;

        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual AcademicTerm? AcademicTerm { get; set; }
    }

    public enum AcademicCalendarEventType
    {
        Academic = 1,
        Holiday = 2,
        Examination = 3,
        Admission = 4,
        Sports = 5,
        Cultural = 6,
        Meeting = 7,
        Other = 8
    }
}