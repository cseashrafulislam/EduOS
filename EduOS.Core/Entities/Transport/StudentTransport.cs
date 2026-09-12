using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Students;
using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.Entities.Transport;

public class StudentTransport : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long VehicleId { get; set; }
    public long RouteId { get; set; }
    public string? PickupPoint { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal MonthlyFare { get; set; }
    public bool IsActive { get; set; } = true;
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public virtual Student? Student { get; set; }
    public virtual Vehicle? Vehicle { get; set; }
    public virtual Route? Route { get; set; }
}
