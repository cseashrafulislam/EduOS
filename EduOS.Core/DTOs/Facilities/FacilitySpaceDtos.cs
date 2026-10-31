using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Facilities;

public sealed class FacilitySpaceDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveFacilitySpaceRequestDto
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
