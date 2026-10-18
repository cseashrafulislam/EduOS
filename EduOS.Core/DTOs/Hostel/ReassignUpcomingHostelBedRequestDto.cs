using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

/// <summary>Changes only the bed of a future reservation, within its existing room.</summary>
public sealed class ReassignUpcomingHostelBedRequestDto
{
    [Range(1, long.MaxValue)] public long TargetBedId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
