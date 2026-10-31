namespace EduOS.Core.DTOs.Transport;

public sealed class ShortenUpcomingNoChargeTransportRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    /// <summary>Required when the reservation has a fare or any posted Finance exposure.</summary>
    public long? FinanceClearanceId { get; set; }
}
