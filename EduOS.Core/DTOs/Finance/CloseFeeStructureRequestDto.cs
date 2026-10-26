namespace EduOS.Core.DTOs.Finance;

/// <summary>Forward-only, inclusive month-end closure of a canonical fee structure.</summary>
public sealed class CloseFeeStructureRequestDto
{
    public DateOnly EffectiveTo { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
