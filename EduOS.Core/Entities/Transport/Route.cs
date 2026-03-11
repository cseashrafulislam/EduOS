using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Transport
{
    public class Route : TenantEntity
    {
        public string Name { get; set; }
        public decimal Fare { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
