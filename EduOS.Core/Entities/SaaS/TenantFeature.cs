using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class TenantFeature : TenantEntity
    {
        public int FeatureId { get; set; }
        public Feature Feature { get; set; } = null!;

        public bool IsEnabled { get; set; } = true;
    }
}