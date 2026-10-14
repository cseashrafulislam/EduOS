using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

/// <summary>Non-monetary route setup; fares remain Finance-owned.</summary>
public sealed class CreateTransportRouteRequestDto
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required] public List<CreateTransportRouteStopDto> Stops { get; set; } = new();
}
public sealed class CreateTransportRouteStopDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
}
public sealed class TransportCampusOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
public sealed class TransportRouteSetupRowDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int StopCount { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
