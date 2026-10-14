using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;
public sealed class CreateTransportVehicleRequestDto
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, MaxLength(50)] public string VehicleNumber { get; set; } = string.Empty;
    [MaxLength(100)] public string? VehicleType { get; set; }
    [Range(1, 1000)] public int Capacity { get; set; }
}
public sealed class UpdateTransportVehicleRequestDto
{
    [Range(1, 1000)] public int Capacity { get; set; }
    public bool IsActive { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class TransportVehicleSetupRowDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public int Capacity { get; set; }
    public int AssignedStudents { get; set; }
    public int CurrentRouteAssignments { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
