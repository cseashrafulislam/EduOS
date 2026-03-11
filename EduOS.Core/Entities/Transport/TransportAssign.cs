using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Transport
{
    public class TransportAssign : TenantEntity
    {
        public int VehicleId { get; set; }
        public int RouteId { get; set; }
        public int? StudentId { get; set; }
        public DateTime AssignDate { get; set; }
    }
}
