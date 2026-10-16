using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Transport;

public sealed class AppendTransportRouteStopRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
}
