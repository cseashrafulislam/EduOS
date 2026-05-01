using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Language : BaseEntity
    {
        public string Code { get; set; } = string.Empty; // en/bn/ar
        public string Name { get; set; } = string.Empty;
        public bool IsRTL { get; set; } = false;
        public bool IsActive { get; set; } = true;
    }
}
