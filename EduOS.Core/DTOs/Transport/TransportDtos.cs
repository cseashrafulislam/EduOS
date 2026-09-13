namespace EduOS.Core.DTOs.Transport;

public sealed class TransportRouteDto
{
    public Guid Reference { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Distance { get; set; }
    public decimal Fare { get; set; }
    public string? Description { get; set; }
}

public sealed class TransportVehicleDto
{
    public Guid Reference { get; set; }
    public string VehicleNo { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int ActiveAssignments { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public Guid? RouteReference { get; set; }
    public string? RouteName { get; set; }
}

public sealed class AssignTransportDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    public Guid VehicleReference { get; set; }
    public Guid RouteReference { get; set; }
    public string? PickupPoint { get; set; }
    public DateTime StartDate { get; set; }
    public decimal? MonthlyFare { get; set; }
}

public sealed class CloseTransportDto
{
    public DateTime? EndDate { get; set; }
}

public sealed class StudentTransportDto
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public Guid VehicleReference { get; set; }
    public string VehicleNo { get; set; } = string.Empty;
    public Guid RouteReference { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string? PickupPoint { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal MonthlyFare { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
