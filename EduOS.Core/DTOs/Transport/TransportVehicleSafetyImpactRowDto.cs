namespace EduOS.Core.DTOs.Transport;

/// <summary>Authorized fleet-level safety impact, not a dispatch or rider attendance record.</summary>
public sealed class TransportVehicleSafetyImpactRowDto
{
    public Guid VehicleReference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public DateOnly OperatingDate { get; set; }
    public int Capacity { get; set; }
    public int ReservedRiders { get; set; }
    public int OpenCases { get; set; }
    public int HighSeverityCases { get; set; }
    public bool CrossCampusCaseHold { get; set; }
    public bool VehicleActive { get; set; }
    public string SafetyState { get; set; } = string.Empty;
}
