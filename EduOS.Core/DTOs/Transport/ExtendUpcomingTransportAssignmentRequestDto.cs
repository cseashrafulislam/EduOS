namespace EduOS.Core.DTOs.Transport;

public sealed class ExtendUpcomingTransportAssignmentRequestDto
{
    [Required] public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
