using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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

            var stored = new StoredPreferences
            {
                SchemaVersion = 1,
                Theme = request.Theme == "system" ? null : request.Theme,
                Palette = request.Palette == "blue" ? null : request.Palette,
                Navigation = request.Navigation == "left" ? null : request.Navigation,
                Density = request.Density == "comfortable" ? null : request.Density,
                FontScale = request.FontScale == "normal" ? null : request.FontScale
            };
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
        SaveAsync(new SaveWorkspacePreferencesRequestDto { RowVersion = request?.RowVersion }, cancellationToken);

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
        (request.RowVersion?.Length ?? 0) <= 100;

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
        try
        {
            var stored = JsonSerializer.Deserialize<StoredPreferences>(row.Value);
            if (stored?.SchemaVersion != 1) return result;
            result.Theme = IsOneOf(stored.Theme, "light", "dark") ? stored.Theme! : "system";
            result.Palette = IsOneOf(stored.Palette, "teal", "violet") ? stored.Palette! : "blue";
            result.Navigation = IsOneOf(stored.Navigation, "right", "top") ? stored.Navigation! : "left";
            result.Density = stored.Density == "compact" ? "compact" : "comfortable";
            result.FontScale = stored.FontScale == "large" ? "large" : "normal";
        }
        catch (JsonException) { /* Retired or corrupt preferences fall back to safe defaults. */ }
        return result;
    }

    private sealed class StoredPreferences
    {
        public int SchemaVersion { get; set; } = 1;
        public string? Theme { get; set; }
        public string? Palette { get; set; }
        public string? Navigation { get; set; }
        public string? Density { get; set; }
        public string? FontScale { get; set; }
    }
}
