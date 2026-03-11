using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Transport
{
    public class Stopage : TenantEntity
    {
        public int RouteId { get; set; }
        public string Name { get; set; }
        public decimal Fare { get; set; }
    }
}
