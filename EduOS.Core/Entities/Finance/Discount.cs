using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Finance
{
    public class Discount : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Percentage";
        public decimal Value { get; set; }
        public long? FeeHeadId { get; set; }
        public bool IsActive { get; set; } = true;
        public virtual FeeHead? FeeHead { get; set; }
    }
}
