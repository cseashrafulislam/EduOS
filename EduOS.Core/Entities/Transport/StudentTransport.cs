using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;

namespace EduOS.Core.Entities.Transport
{
    public class StudentTransport : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int VehicleId { get; set; }
        public int RouteId { get; set; }
        public string? PickupPoint { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal MonthlyFare { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Student? Student { get; set; }
        public virtual Vehicle? Vehicle { get; set; }
        public virtual Route? Route { get; set; }
    }
}
