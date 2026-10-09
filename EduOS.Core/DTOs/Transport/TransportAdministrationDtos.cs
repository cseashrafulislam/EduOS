namespace EduOS.Core.DTOs.Transport;

public sealed class TransportDriverDto
{
    public long Id { get; set; }
    public long? EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? LicenseNumber { get; set; }
    public DateOnly? LicenseExpiresOn { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveTransportDriverRequestDto
{
    public long? EmployeeId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = string.Empty;
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(100)] public string? LicenseNumber { get; set; }
    public DateOnly? LicenseExpiresOn { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class VehicleDriverAssignmentDto
{
    public long Id { get; set; }
    public long VehicleId { get; set; }
    public long TransportDriverId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssignVehicleDriverRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long VehicleId { get; set; }
    [Range(1, long.MaxValue)] public long TransportDriverId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class RouteVehicleAssignmentDto
{
    public long Id { get; set; }
    public long RouteId { get; set; }
    public long VehicleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsCurrent { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssignRouteVehicleRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long RouteId { get; set; }
    [Range(1, long.MaxValue)] public long VehicleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class CloseTransportAssignmentRequestDto
{
    public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
