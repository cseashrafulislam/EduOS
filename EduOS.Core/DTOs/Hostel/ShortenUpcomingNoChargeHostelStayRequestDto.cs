namespace EduOS.Core.DTOs.Hostel;

public sealed class ShortenUpcomingNoChargeHostelStayRequestDto
{
    public DateOnly EndDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
