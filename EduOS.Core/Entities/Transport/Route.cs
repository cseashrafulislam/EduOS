using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport
{
    public class Route : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public decimal Distance { get; set; } // KM
        public decimal Fare { get; set; } // Monthly
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
