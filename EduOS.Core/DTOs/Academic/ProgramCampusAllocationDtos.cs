using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Academic;

/// <summary>Effective programme campus availability; an empty allocation means all tenant campuses.</summary>
public sealed class ProgramCampusAllocationDto
{
    public long AcademicProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramCode { get; set; } = string.Empty;
    public IReadOnlyList<long> CampusIds { get; set; } = Array.Empty<long>();
    public long? PrimaryCampusId { get; set; }
    public bool AllowAllCampuses { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

/// <summary>Full replacement of active campus assignments, guarded by the programme row version.</summary>
public sealed class UpdateProgramCampusAllocationRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<long> CampusIds { get; set; } = Array.Empty<long>();
    public long? PrimaryCampusId { get; set; }
    public bool AllowAllCampuses { get; set; }
}
