using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants;

/// <summary>
/// A private, bounded user/tenant namespace in the existing versioned TenantSettings table.
/// The general institution settings API must never expose or modify this namespace.
/// </summary>
public sealed class WorkspacePreferencesService : IWorkspacePreferencesService
{
    public const string KeyPrefix = "Workspace.User.";
    private const string Category = "UserWorkspace";
    private static readonly HashSet<string> ColorTokens = new(StringComparer.Ordinal)
    { "sidebar", "header", "page", "card", "button", "form", "table" };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IGenericRepository<TenantSetting> _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _user;
    private readonly ILogger<WorkspacePreferencesService> _logger;

    public WorkspacePreferencesService(IGenericRepository<TenantSetting> settings, IUnitOfWork unitOfWork,
        ICurrentUserService user, ILogger<WorkspacePreferencesService> logger)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _user = user;
        _logger = logger;
    }

    public async Task<ApiResponse<WorkspacePreferencesDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!HasTenantMembership())
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("An active institution session is required.", 403);
        try
        {
            var row = await FindAsync(tracked: false, cancellationToken);
            return ApiResponse<WorkspacePreferencesDto>.SuccessResponse(ToDto(row));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workspace preferences read failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Workspace preferences could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<WorkspacePreferencesDto>> SaveAsync(SaveWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!HasTenantMembership())
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("An active institution session is required.", 403);
        if (!IsValid(request))
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Unsupported workspace preference value.", 400);
        try
        {
            var row = await FindAsync(tracked: true, cancellationToken);
            if (row == null && !string.IsNullOrEmpty(request.RowVersion))
                return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Workspace changed. Reload before saving.", 409);
            if (row != null && !MatchesVersion(row.RowVersion, request.RowVersion))
                return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Workspace changed on another device. Reload before saving.", 409);
            if (row != null && (row.Category != Category || row.IsSensitive))
                return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Reserved workspace key conflict.", 409);

            // Older cached clients do not send tablePreferences; never erase newer layout choices.
            // Older clients may submit only the first two or four table layouts.
            // Merge those into the current versioned row without erasing newer layouts.
            var previous = ToDto(row);
            var currentTables = previous.TablePreferences;
            var tables = request.TablePreferences == null ? currentTables :
                request.TablePreferences.Count < TablePreferenceDto.Defaults().Count
                    ? MergeTables(currentTables, request.TablePreferences)
                    : request.TablePreferences;
            var colors = request.CustomColors ?? previous.CustomColors;
            var stored = new StoredPreferences
            {
                SchemaVersion = 1,
                Theme = request.Theme == "system" ? null : request.Theme,
                Palette = request.Palette == "blue" ? null : request.Palette,
                Navigation = request.Navigation == "left" ? null : request.Navigation,
                Density = request.Density == "comfortable" ? null : request.Density,
                FontScale = request.FontScale == "normal" ? null : request.FontScale,
                CustomColors = colors.Count == 0 ? null : colors.ToDictionary(x => x.Key, x => NormalizeColor(x.Value)!, StringComparer.Ordinal),
                DashboardPanels = request.DashboardPanels.Select(x => new DashboardPanelPreferenceDto
                {
                    Id = x.Id, Visible = x.Visible
                }).ToList(),
                TablePreferences = tables.Select(table => new TablePreferenceDto
                {
                    Id = table.Id,
                    Columns = table.Columns.Select(column => new TableColumnPreferenceDto
                    {
                        Id = column.Id, Visible = column.Visible
                    }).ToList()
                }).ToList()
            };
            // Archive only normalized prior settings, bounded in the existing private versioned row.
            var priorStored = ReadStored(row);
            var priorRevision = Math.Max(1L, priorStored?.Revision ?? 1L);
            if (row != null && priorRevision == long.MaxValue)
                return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Appearance revision limit reached.", 409);
            stored.Revision = row == null ? 1 : priorRevision + 1;
            var history = ReadHistory(priorStored);
            if (row != null && priorStored != null)
                history.Add(new StoredAppearanceRevision
                {
                    Revision = priorRevision,
                    SavedAtUtc = row.UpdatedAt ?? row.CreatedAt,
                    Snapshot = Snapshot(previous)
                });
            stored.History = history.TakeLast(5).ToList();
            if (row == null)
            {
                row = new TenantSetting
                {
                    TenantId = _user.TenantId,
                    Key = UserKey(),
                    Category = Category,
                    Value = JsonSerializer.Serialize(stored, JsonOptions),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _user.UserId
                };
                await _settings.AddAsync(row);
            }
            else
            {
                row.Value = JsonSerializer.Serialize(stored, JsonOptions);
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = _user.UserId;
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<WorkspacePreferencesDto>.SuccessResponse(ToDto(row));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Workspace changed on another device. Reload before saving.", 409);
        }
        catch (DbUpdateException)
        {
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Workspace update conflicted with another request. Reload.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workspace preferences save failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Workspace preferences could not be saved.", 500);
        }
    }

    public Task<ApiResponse<WorkspacePreferencesDto>> ResetAsync(ResetWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken = default) =>
        SaveAsync(new SaveWorkspacePreferencesRequestDto
        {
            RowVersion = request?.RowVersion,
            TablePreferences = TablePreferenceDto.Defaults(),
            CustomColors = []
        }, cancellationToken);

    public async Task<ApiResponse<WorkspacePreferencesDto>> RestoreAsync(RestoreWorkspacePreferencesRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!HasTenantMembership())
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("An active institution session is required.", 403);
        if (request == null || request.Revision <= 0 || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length > 100)
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Invalid appearance revision.", 400);
        try
        {
            var row = await FindAsync(tracked: false, cancellationToken);
            if (row == null)
                return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Appearance history not found.", 404);
            if (!MatchesVersion(row.RowVersion, request.RowVersion))
                return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Workspace changed on another device. Reload before restoring.", 409);
            var snapshot = ReadHistory(ReadStored(row)).FirstOrDefault(x => x.Revision == request.Revision)?.Snapshot;
            if (snapshot == null)
                return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Appearance revision not found.", 404);
            // SaveAsync rechecks rowversion under the controller's SERIALIZABLE session-bound transaction.
            return await SaveAsync(RestoreRequest(snapshot, request.RowVersion), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workspace history restore failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<WorkspacePreferencesDto>.ErrorResponse("Appearance could not be restored.", 500);
        }
    }

    private static StoredPreferences Snapshot(WorkspacePreferencesDto source) => new()
    {
        SchemaVersion = 1, Revision = source.Revision,
        Theme = source.Theme, Palette = source.Palette, Navigation = source.Navigation,
        Density = source.Density, FontScale = source.FontScale,
        CustomColors = source.CustomColors, DashboardPanels = source.DashboardPanels,
        TablePreferences = source.TablePreferences
    };

    private static SaveWorkspacePreferencesRequestDto RestoreRequest(StoredPreferences snapshot, string? rowVersion) => new()
    {
        RowVersion = rowVersion,
        Theme = snapshot.Theme ?? "system", Palette = snapshot.Palette ?? "blue",
        Navigation = snapshot.Navigation ?? "left", Density = snapshot.Density ?? "comfortable",
        FontScale = snapshot.FontScale ?? "normal", CustomColors = snapshot.CustomColors ?? [],
        DashboardPanels = snapshot.DashboardPanels ?? DashboardPanelPreferenceDto.Defaults(),
        TablePreferences = snapshot.TablePreferences ?? TablePreferenceDto.Defaults()
    };

    private static StoredPreferences? ReadStored(TenantSetting? row)
    {
        if (row == null || row.Category != Category || row.IsSensitive) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredPreferences>(row.Value);
            return stored?.SchemaVersion == 1 ? stored : null;
        }
        catch (JsonException) { return null; }
    }

    private static List<StoredAppearanceRevision> ReadHistory(StoredPreferences? stored) =>
        stored?.History?.Where(entry => entry != null && entry.Revision > 0 &&
                entry.Revision < stored.Revision && entry.Snapshot is { SchemaVersion: 1, History: null } &&
                IsValid(RestoreRequest(entry.Snapshot!, null)))
            .GroupBy(entry => entry.Revision).Select(group => group.First()).TakeLast(5).ToList() ?? [];

    private bool HasTenantMembership() => _user.IsAuthenticated && _user.TenantId > 0 && _user.UserId > 0;

    private string UserKey() => KeyPrefix + _user.UserId.ToString(CultureInfo.InvariantCulture);

    private Task<TenantSetting?> FindAsync(bool tracked, CancellationToken cancellationToken)
    {
        var query = _settings.GetQueryable().Where(x => x.TenantId == _user.TenantId && x.Key == UserKey());
        if (!tracked) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsValid(SaveWorkspacePreferencesRequestDto? request) =>
        request != null && request.SchemaVersion == 1 &&
        IsOneOf(request.Theme, "system", "light", "dark") &&
        IsOneOf(request.Palette, "blue", "teal", "violet") &&
        IsOneOf(request.Navigation, "left", "right", "top") &&
        IsOneOf(request.Density, "comfortable", "compact") &&
        IsOneOf(request.FontScale, "normal", "large") &&
        (request.RowVersion?.Length ?? 0) <= 100 && ValidColors(request.CustomColors) && ValidPanels(request.DashboardPanels) &&
        (request.TablePreferences == null || ValidTables(request.TablePreferences));

    private static bool ValidColors(Dictionary<string, string>? colors) =>
        colors == null || (colors.Count <= ColorTokens.Count &&
            colors.All(x => ColorTokens.Contains(x.Key) && NormalizeColor(x.Value) != null));

    private static string? NormalizeColor(string? input)
    {
        if (input == null || input.Length > 32) return null;
        var value = input.Trim();
        if (Regex.IsMatch(value, @"^#[0-9a-fA-F]{6}$")) return value.ToUpperInvariant();
        if (Regex.IsMatch(value, @"^#[0-9a-fA-F]{3}$"))
            return "#" + string.Concat(value.Skip(1).Select(c => new string(char.ToUpperInvariant(c), 2)));
        var rgb = Regex.Match(value, @"^rgb\(\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!rgb.Success) return null;
        var c = rgb.Groups.Cast<Group>().Skip(1).Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
        return c.Any(x => x > 255) ? null : $"#{c[0]:X2}{c[1]:X2}{c[2]:X2}";
    }

    private static bool ValidPanels(List<DashboardPanelPreferenceDto>? panels) =>
        panels is { Count: 3 } &&
        panels.All(x => x != null && x.Id is "capacity" or "quick-actions" or "setup-checklist") &&
        panels.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() == 3;

    private static bool ValidTables(List<TablePreferenceDto>? tables)
    {
        var defaults = TablePreferenceDto.Defaults();
        // The first two tables are the exact legacy client contract. A partial
        // new-client layout is invalid; it must not silently hide required columns.
        if (tables is not { Count: 2 or 4 or 6 or 8 } ||
            tables.Select(x => x?.Id).Distinct(StringComparer.Ordinal).Count() != tables.Count)
            return false;
        var definitions = defaults.Take(tables.Count).ToList();
        foreach (var table in tables)
        {
            var definition = definitions.FirstOrDefault(x => x.Id == table?.Id);
            if (definition == null || table!.Columns == null ||
                table.Columns.Count != definition.Columns.Count ||
                table.Columns.Any(x => x == null ||
                    !definition.Columns.Any(allowed => allowed.Id == x.Id)) ||
                table.Columns.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != definition.Columns.Count)
                return false;
            var required = table.Id switch
            {
                "institution-users" => new[] { "user", "action" },
                "institution-invitations" => new[] { "email", "status", "action" },
                "institution-feature-audit" => new[] { "feature", "code", "restriction" },
                "institution-activity-audit" => new[] { "time", "user", "action", "outcome" },
                "institution-campuses" => new[] { "campus", "code", "action" },
                "institution-change-history" => new[] { "when", "action", "actor" },
                "institution-settings" => new[] { "key", "protection", "action" },
                "institution-permission-exceptions" => new[] { "permission", "mode", "expires", "action" },
                _ => Array.Empty<string>()
            };
            if (required.Any(id => !table.Columns.Single(x => x.Id == id).Visible))
                return false;
        }
        return true;
    }

    private static List<TablePreferenceDto> MergeTables(
        List<TablePreferenceDto> current, List<TablePreferenceDto> incoming) =>
        TablePreferenceDto.Defaults().Select(definition =>
            incoming.FirstOrDefault(x => x.Id == definition.Id) ??
            current.FirstOrDefault(x => x.Id == definition.Id) ?? definition).ToList();

    private static bool IsOneOf(string? value, params string[] allowed) =>
        value != null && allowed.Contains(value, StringComparer.Ordinal);

    private static bool MatchesVersion(byte[] current, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(encoded)); }
        catch (FormatException) { return false; }
    }

    private static WorkspacePreferencesDto ToDto(TenantSetting? row)
    {
        var result = new WorkspacePreferencesDto();
        if (row == null) return result;
        result.RowVersion = Convert.ToBase64String(row.RowVersion);
        if (row.Category != Category || row.IsSensitive) return result;
        var stored = ReadStored(row);
        if (stored == null) return result;
        result.Revision = Math.Max(1L, stored.Revision);
        result.History = ReadHistory(stored).Select(entry => new WorkspaceAppearanceRevisionDto
        {
            Revision = entry.Revision, SavedAtUtc = entry.SavedAtUtc
        }).ToList();
        try
        {
            result.Theme = IsOneOf(stored.Theme, "light", "dark") ? stored.Theme! : "system";
            result.Palette = IsOneOf(stored.Palette, "teal", "violet") ? stored.Palette! : "blue";
            result.Navigation = IsOneOf(stored.Navigation, "right", "top") ? stored.Navigation! : "left";
            result.Density = stored.Density == "compact" ? "compact" : "comfortable";
            result.FontScale = stored.FontScale == "large" ? "large" : "normal";
            if (stored.CustomColors != null && ValidColors(stored.CustomColors))
                result.CustomColors = stored.CustomColors.ToDictionary(x => x.Key, x => NormalizeColor(x.Value)!, StringComparer.Ordinal);
            if (ValidPanels(stored.DashboardPanels))
                result.DashboardPanels = stored.DashboardPanels!.Select(x => new DashboardPanelPreferenceDto
                {
                    Id = x.Id, Visible = x.Visible
                }).ToList();
            if (ValidTables(stored.TablePreferences))
                result.TablePreferences = MergeTables(TablePreferenceDto.Defaults(),
                    stored.TablePreferences!).Select(table => new TablePreferenceDto
                {
                    Id = table.Id,
                    Columns = table.Columns.Select(column => new TableColumnPreferenceDto
                    {
                        Id = column.Id, Visible = column.Visible
                    }).ToList()
                }).ToList();
        }
        catch (JsonException) { /* Retired or corrupt preferences fall back to safe defaults. */ }
        return result;
    }

    private sealed class StoredAppearanceRevision
    {
        public long Revision { get; set; }
        public DateTime SavedAtUtc { get; set; }
        public StoredPreferences? Snapshot { get; set; }
    }

    private sealed class StoredPreferences
    {
        public int SchemaVersion { get; set; } = 1;
        public long Revision { get; set; }
        public List<StoredAppearanceRevision>? History { get; set; }
        public string? Theme { get; set; }
        public string? Palette { get; set; }
        public string? Navigation { get; set; }
        public string? Density { get; set; }
        public string? FontScale { get; set; }
        public Dictionary<string, string>? CustomColors { get; set; }
        public List<DashboardPanelPreferenceDto>? DashboardPanels { get; set; }
        public List<TablePreferenceDto>? TablePreferences { get; set; }
    }
}
