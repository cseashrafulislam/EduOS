using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class RoutineTimeSlot : BaseTenantEntity
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public bool IsBreak { get; set; }
        public bool IsActive { get; set; } = true;

        public int DisplayOrder { get; set; } = 1;
    }
}