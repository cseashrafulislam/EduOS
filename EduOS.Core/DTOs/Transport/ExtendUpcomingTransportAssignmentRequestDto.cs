namespace EduOS.Core.DTOs.Transport;

public sealed class ExtendUpcomingTransportAssignmentRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    /// <summary>Single-use Finance approval for a charge-bearing period amendment.</summary>
    public long? FinanceClearanceId { get; set; }
}
