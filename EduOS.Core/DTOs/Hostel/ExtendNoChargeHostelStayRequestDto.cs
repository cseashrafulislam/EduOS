namespace EduOS.Core.DTOs.Hostel;

public sealed class ExtendNoChargeHostelStayRequestDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    /// <summary>Single-use approval for a charged or previously posted booking.</summary>
    public long? FinanceClearanceId { get; set; }
}
