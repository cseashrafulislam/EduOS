namespace EduOS.Core.DTOs.Academic;

/// <summary>Existing offering identity, batch, curriculum subject, year, term and code are immutable.</summary>
public sealed class UpdateSubjectOfferingRequestDto
{
    [Range(1, 100000)] public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
