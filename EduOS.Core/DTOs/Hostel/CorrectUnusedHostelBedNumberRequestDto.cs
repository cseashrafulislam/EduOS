namespace EduOS.Core.DTOs.Hostel;

public sealed class CorrectUnusedHostelBedNumberRequestDto
{
    [Required, MaxLength(50)] public string BedNumber { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
