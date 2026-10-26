using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

/// <summary>Pre-arrival date correction only; no financial terms or bed changes.</summary>
public sealed class RescheduleUpcomingHostelStartRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public DateOnly StartDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
