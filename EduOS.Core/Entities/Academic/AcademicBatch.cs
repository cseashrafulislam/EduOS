using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
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

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.OnCampus;

        public int Capacity { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; } = 1;

        [MaxLength(500)]
        public string? Remarks { get; set; }

        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual AcademicTerm? AcademicTerm { get; set; }
        public virtual AcademicProgram? AcademicProgram { get; set; }
        public virtual AcademicLevel? AcademicLevel { get; set; }
        public virtual AcademicTrack? AcademicTrack { get; set; }
        public virtual Medium? Medium { get; set; }
        public virtual Shift? Shift { get; set; }
    }

    public enum DeliveryMode
    {
        OnCampus = 1,
        OnlineLive = 2,
        OnlineSelfPaced = 3,
        Hybrid = 4
    }
}