using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Transport
{
    public class Vehicle : TenantEntity
    {
        public string VehicleNo { get; set; }
        public string VehicleType { get; set; }
        public int SeatCapacity { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
