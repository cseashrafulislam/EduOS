namespace EduOS.Core.DTOs.Finance;

/// <summary>Bounded institutional fee-structure register; financial lines are read from the detail endpoint.</summary>
public sealed class FeeStructureRegisterDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicLevelId { get; set; }
    public long? AcademicBatchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "BDT";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
}
