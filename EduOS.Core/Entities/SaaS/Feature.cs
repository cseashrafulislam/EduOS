using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    public class Feature : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty; // LMS/LIB/HSTL/TRNS/SMS/MOB
        public string? Description { get; set; }
        public string Category { get; set; } = "Module";
        public bool IsActive { get; set; } = true;
    }
}
