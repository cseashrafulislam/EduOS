using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Facilities;

/// <summary>Campus-owned operational space; distinct from academic rooms, hostel beds and inventory locations.</summary>
public sealed class FacilitySpace : BaseTenantEntity
{
    public long CampusId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}
