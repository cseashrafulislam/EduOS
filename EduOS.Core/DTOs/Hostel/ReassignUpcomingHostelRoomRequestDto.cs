using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

/// <summary>Pre-arrival room correction within the same hostel and tariff; Finance values are immutable.</summary>
public sealed class ReassignUpcomingHostelRoomRequestDto
{
    [Range(1, long.MaxValue)] public long TargetBedId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
