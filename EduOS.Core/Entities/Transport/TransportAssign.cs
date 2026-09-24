using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport;

public class TransportAssign : BaseTenantEntity
{
    public long VehicleId { get; set; }
    public long RouteId { get; set; }
    public long? StudentId { get; set; }
    public DateTime AssignDate { get; set; }
}
