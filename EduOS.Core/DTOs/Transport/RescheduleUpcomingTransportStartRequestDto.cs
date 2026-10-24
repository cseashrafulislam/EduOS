namespace EduOS.Core.DTOs.Transport;

public sealed class RescheduleUpcomingTransportStartRequestDto
{
    public DateOnly StartDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
