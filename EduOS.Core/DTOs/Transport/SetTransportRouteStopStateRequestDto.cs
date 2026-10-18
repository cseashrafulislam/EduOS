using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;

/// <summary>Non-monetary operational state for a canonical RouteStop child.</summary>
public sealed class SetTransportRouteStopStateRequestDto
{
    public bool IsActive { get; set; }
    [Required] public string RouteRowVersion { get; set; } = string.Empty;
    [Required] public string StopRowVersion { get; set; } = string.Empty;
}
