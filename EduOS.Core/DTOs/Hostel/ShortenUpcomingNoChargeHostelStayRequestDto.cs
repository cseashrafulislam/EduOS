namespace EduOS.Core.DTOs.Hostel;

public sealed class ShortenUpcomingNoChargeHostelStayRequestDto
{
    public Guid ClientRequestId { get; set; }
    public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
