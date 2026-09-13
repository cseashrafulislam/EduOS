using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport;

public class Stopage : BaseTenantEntity
{
    public long RouteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Fare { get; set; }
}
