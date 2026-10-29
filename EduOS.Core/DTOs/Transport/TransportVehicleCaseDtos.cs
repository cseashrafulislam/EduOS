using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

public sealed class CreateTransportVehicleCaseRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid VehicleReference { get; set; }
    public DateOnly OccurredOn { get; set; }
    [Required, MaxLength(20)] public string CaseType { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Severity { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Summary { get; set; } = string.Empty;
}

public sealed class ResolveTransportVehicleCaseRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string Resolution { get; set; } = string.Empty;
}

public sealed class TransportVehicleCaseRowDto
{
    public Guid Reference { get; set; }
    public Guid VehicleReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public string CaseType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
