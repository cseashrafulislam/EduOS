namespace EduOS.Core.DTOs.Transport;

public class RouteDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal DefaultFare { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<RouteStopDto> Stops { get; set; } = Array.Empty<RouteStopDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveRouteRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(typeof(decimal), "0", "999999999999")] public decimal DefaultFare { get; set; }
    public bool IsActive { get; set; } = true;
    public IReadOnlyList<SaveRouteStopRequestDto> Stops { get; set; } = Array.Empty<SaveRouteStopRequestDto>();
    public string? RowVersion { get; set; }
}

public class RouteStopDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SequenceNo { get; set; }
    public decimal? FareFromOrigin { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveRouteStopRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Range(1, 10000)] public int SequenceNo { get; set; }
    [Range(typeof(decimal), "0", "999999999999")] public decimal? FareFromOrigin { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class VehicleDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public int Capacity { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public bool IsActive { get; set; }
    public int ActiveAssignments { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveVehicleRequestDto
{
    [Required, MaxLength(50)] public string VehicleNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? VehicleType { get; set; }
    [Range(1, 1000)] public int Capacity { get; set; }
    [MaxLength(150)] public string? DriverName { get; set; }
    [MaxLength(30)] public string? DriverPhone { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class StudentTransportDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public Guid RouteReference { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public Guid VehicleReference { get; set; }
    public string VehicleNumber { get; set; } = string.Empty;
    public long? PickupStopId { get; set; }
    public string? PickupStopName { get; set; }
    public long? DropStopId { get; set; }
    public string? DropStopName { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal MonthlyFare { get; set; }
    public TransportAssignmentState State { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class AssignStudentTransportRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentEnrollmentReference { get; set; }
    public Guid RouteReference { get; set; }
    public Guid VehicleReference { get; set; }
    public long? PickupStopId { get; set; }
    public long? DropStopId { get; set; }
    public DateOnly StartDate { get; set; }
    public decimal? MonthlyFare { get; set; }
}

public class CloseStudentTransportRequestDto
{
    public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
