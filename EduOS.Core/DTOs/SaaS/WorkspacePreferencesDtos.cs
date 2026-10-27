namespace EduOS.Core.DTOs.SaaS;

/// <summary>Versioned, non-sensitive appearance choices for one user in one institution.</summary>
public sealed class WorkspacePreferencesDto
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "system";
    public string Palette { get; set; } = "blue";
    public string Navigation { get; set; } = "left";
    public string Density { get; set; } = "comfortable";
    public string FontScale { get; set; } = "normal";
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveWorkspacePreferencesRequestDto
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "system";
    public string Palette { get; set; } = "blue";
    public string Navigation { get; set; } = "left";
    public string Density { get; set; } = "comfortable";
    public string FontScale { get; set; } = "normal";
    public string? RowVersion { get; set; }
}

public sealed class ResetWorkspacePreferencesRequestDto
{
    public string? RowVersion { get; set; }
}
