using EduOS.Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Academic
{
    public class Room : BaseTenantEntity
    {
        public long? CampusId { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? BuildingName { get; set; }

        [MaxLength(50)]
        public string? Floor { get; set; }

        public int Capacity { get; set; }

        public bool IsLab { get; set; }
        public bool IsActive { get; set; } = true;
    }
}