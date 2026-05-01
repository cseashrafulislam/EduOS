using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport
{
    public class TransportAssign : BaseTenantEntity
    {
        public int VehicleId { get; set; }
        public int RouteId { get; set; }
        public int? StudentId { get; set; }
        public DateTime AssignDate { get; set; }
    }
}
