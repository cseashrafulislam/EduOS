using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Transport;

/// <summary>Operational state only; Finance owns fares.</summary>
public sealed class SetTransportRouteStateRequestDto
{
    public bool IsActive { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
