using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Transport;

public class Route : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long? CampusId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")] public decimal DefaultFare { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RouteStop : BaseTenantEntity
{
    public long RouteId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public int SequenceNo { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? FareFromOrigin { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Vehicle : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long? CampusId { get; set; }
    [Required, MaxLength(50)] public string VehicleNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? VehicleType { get; set; }
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StudentTransport : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public long StudentId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public long? RouteVehicleAssignmentId { get; set; }
    public long RouteId { get; set; }
    public long VehicleId { get; set; }
    public long? PickupStopId { get; set; }
    public long? DropStopId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MonthlyFare { get; set; }
    public TransportAssignmentState State { get; set; } = TransportAssignmentState.Active;
}

public class TransportDriver : BaseTenantEntity
{
    public long? EmployeeId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(100)] public string? LicenseNumber { get; set; }
    public DateOnly? LicenseExpiresOn { get; set; }
    public bool IsActive { get; set; } = true;
}

public class VehicleDriverAssignment : BaseTenantEntity
{
    public long VehicleId { get; set; }
    public long TransportDriverId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; } = true;
}

public class RouteVehicleAssignment : BaseTenantEntity
{
    public long RouteId { get; set; }
    public long VehicleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; } = true;
}
