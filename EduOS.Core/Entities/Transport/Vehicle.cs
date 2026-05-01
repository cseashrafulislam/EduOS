using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport
{
    public class Vehicle : BaseTenantEntity
    {
        public string VehicleNo { get; set; } = string.Empty;
        public string Type { get; set; } = "Bus"; // Bus/Van/Car
        public int Capacity { get; set; }
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public int? RouteId { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Route? Route { get; set; }
    }
}
