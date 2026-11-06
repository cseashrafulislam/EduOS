namespace EduOS.Core.DTOs.Platform;

public sealed class CentralInstitutionMisDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public IReadOnlyList<CentralInstitutionMisMetricDto> Metrics { get; set; } =
        Array.Empty<CentralInstitutionMisMetricDto>();
}

public sealed class CentralInstitutionMisMetricDto
{
    public string Key { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public string Scope { get; set; } = "Current";
    public string Unit { get; set; } = "Count";
    // NULL means the module is not entitled, never an observed zero.
    public decimal? Value { get; set; }
    public bool Available { get; set; }
}
