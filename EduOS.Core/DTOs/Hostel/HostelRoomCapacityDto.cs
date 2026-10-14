using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

public sealed class SetHostelRoomCapacityRequestDto
{
    [Range(1, 1000)] public int Capacity { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
