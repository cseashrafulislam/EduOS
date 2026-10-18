using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

/// <summary>Optimistic concurrency token for cancelling a future reservation; no monetary fields.</summary>
public sealed class CancelStudentHostelAllocationRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
