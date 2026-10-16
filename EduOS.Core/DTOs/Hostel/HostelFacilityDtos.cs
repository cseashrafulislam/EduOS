using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Hostel;

public sealed class HostelFacilityDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public string CampusName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? GenderRestriction { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public int ActiveRooms { get; set; }
    public int OccupiedBeds { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class HostelFacilityCampusDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CreateHostelFacilityRequestDto
{
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    [Required, StringLength(150, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(50, MinimumLength = 2)] public string Code { get; set; } = string.Empty;
    [MaxLength(50)] public string? GenderRestriction { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
}

public sealed class UpdateHostelFacilityRequestDto
{
    [Required, StringLength(150, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? GenderRestriction { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class SetHostelFacilityStateRequestDto
{
    public bool IsActive { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
