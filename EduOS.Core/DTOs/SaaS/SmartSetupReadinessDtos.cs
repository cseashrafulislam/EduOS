namespace EduOS.Core.DTOs.SaaS;

/// <summary>Tenant-scoped setup checklist. Readiness is for prerequisite masters, not business approval.</summary>
public sealed class SmartSetupRequirementDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string LabelBangla { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = string.Empty;
    public bool Configured { get; set; }
}

public sealed class SmartSetupReadinessDto
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string ModuleNameBangla { get; set; } = string.Empty;
    public string NextStepUrl { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool PrerequisitesConfigured { get; set; }
    public bool HasChecklist { get; set; }
    public List<SmartSetupRequirementDto> Requirements { get; set; } = new();
}
