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
    public List<DashboardPanelPreferenceDto> DashboardPanels { get; set; } = DashboardPanelPreferenceDto.Defaults();
    public List<TablePreferenceDto> TablePreferences { get; set; } = TablePreferenceDto.Defaults();
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
    public List<DashboardPanelPreferenceDto> DashboardPanels { get; set; } = DashboardPanelPreferenceDto.Defaults();
    // Null means a pre-upgrade client did not send this field; preserve its saved layout.
    public List<TablePreferenceDto>? TablePreferences { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class DashboardPanelPreferenceDto
{
    public string Id { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;

    public static List<DashboardPanelPreferenceDto> Defaults() =>
    [
        new() { Id = "capacity" },
        new() { Id = "quick-actions" },
        new() { Id = "setup-checklist" }
    ];
}

/// <summary>Presentation-only choices for allowlisted institution administration tables.</summary>
public sealed class TablePreferenceDto
{
    public string Id { get; set; } = string.Empty;
    public List<TableColumnPreferenceDto> Columns { get; set; } = [];

    public static List<TablePreferenceDto> Defaults() =>
    [
        new() { Id = "institution-users", Columns =
        [
            new() { Id = "user" }, new() { Id = "membership" },
            new() { Id = "roles" }, new() { Id = "action" }
        ] },
        new() { Id = "institution-invitations", Columns =
        [
            new() { Id = "email" }, new() { Id = "role" },
            new() { Id = "campus" }, new() { Id = "expires" },
            new() { Id = "status" }, new() { Id = "action" }
        ] }
    ];
}

public sealed class TableColumnPreferenceDto
{
    public string Id { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
}

public sealed class ResetWorkspacePreferencesRequestDto
{
    public string? RowVersion { get; set; }
}
