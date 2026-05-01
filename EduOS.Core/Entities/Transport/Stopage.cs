using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport
{
    public class Stopage : BaseTenantEntity
    {
        public int RouteId { get; set; }
        public string Name { get; set; }
        public decimal Fare { get; set; }
    }
}
