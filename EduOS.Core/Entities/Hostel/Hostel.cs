using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Hostel
{
    public class Hostel : TenantEntity
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
