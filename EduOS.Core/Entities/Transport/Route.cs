using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport;

public class Route : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public decimal Distance { get; set; }
    public decimal Fare { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
