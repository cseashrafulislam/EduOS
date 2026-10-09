using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace EduOS.Service.Services.Tenants;

public sealed class TenantSettingService : ITenantSettingService
{
    private const string ProtectedPrefix = "dp:v1:";
    private readonly IGenericRepository<TenantSetting> _settings;
    private readonly IGenericRepository<TenantTerminology> _terminology;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _user;
    private readonly IDataProtector _protector;
    private readonly ILogger<TenantSettingService> _logger;

    public TenantSettingService(IGenericRepository<TenantSetting> settings,
        IGenericRepository<TenantTerminology> terminology, IUnitOfWork unitOfWork,
        ICurrentUserService user, IDataProtectionProvider provider, ILogger<TenantSettingService> logger)
    {
        _settings = settings; _terminology = terminology; _unitOfWork = unitOfWork; _user = user;
        _protector = provider.CreateProtector("EduOS.TenantSettings.SensitiveValues.v1"); _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<TenantSettingDto>>> GetSettingsAsync(string? category, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<PagedResult<TenantSettingDto>>.ErrorResponse("Tenant access is required.", 403);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        try
        {
            var query = _settings.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category.Trim());
            var count = await query.CountAsync(cancellationToken);
            var offset = ((long)page - 1) * pageSize;
            var rows = offset > int.MaxValue ? new List<TenantSetting>() :
                await query.OrderBy(x => x.Key).ThenBy(x => x.Id).Skip((int)offset)
                    .Take(pageSize).ToListAsync(cancellationToken);
            return ApiResponse<PagedResult<TenantSettingDto>>.SuccessResponse(new PagedResult<TenantSettingDto>
            {
                Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant setting query failed for {TenantId}", _user.TenantId);
            return ApiResponse<PagedResult<TenantSettingDto>>.ErrorResponse("Settings could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<TenantSettingDto>> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<TenantSettingDto>.ErrorResponse("Tenant access is required.", 403);
        if (string.IsNullOrWhiteSpace(key)) return ApiResponse<TenantSettingDto>.ErrorResponse("Setting key is required.");
        var row = await _settings.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.Key == key.Trim(), cancellationToken);
        return row == null ? ApiResponse<TenantSettingDto>.ErrorResponse("Setting not found.", 404) :
            ApiResponse<TenantSettingDto>.SuccessResponse(Map(row));
    }

    public async Task<ApiResponse<TenantSettingDto>> SaveSettingAsync(long? settingId, SaveTenantSettingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<TenantSettingDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Key) || request.Key.Trim().Length > 150 ||
            request.Category?.Length > 100)
            return ApiResponse<TenantSettingDto>.ErrorResponse("Setting key or category is invalid.");
        var key = request.Key.Trim();
        if (key.StartsWith("Branding.", StringComparison.OrdinalIgnoreCase) &&
            key is "Branding.PrimaryColor" or "Branding.SecondaryColor" or "Branding.AccentColor" &&
            !Regex.IsMatch(request.Value ?? "", "^#[0-9A-Fa-f]{6}$"))
            return ApiResponse<TenantSettingDto>.ErrorResponse("Branding color must use #RRGGBB.");
        try
        {
            var query = _settings.GetQueryable();
            var existing = settingId.HasValue
                ? await query.FirstOrDefaultAsync(x => x.Id == settingId.Value && x.TenantId == _user.TenantId, cancellationToken)
                : await query.FirstOrDefaultAsync(x => x.Key == key && x.TenantId == _user.TenantId, cancellationToken);
            if (settingId.HasValue && existing == null) return ApiResponse<TenantSettingDto>.ErrorResponse("Setting not found.", 404);
            if (existing != null && settingId.HasValue && !MatchesVersion(existing.RowVersion, request.RowVersion))
                return ApiResponse<TenantSettingDto>.ErrorResponse("Setting changed. Reload and retry.", 409);
            if (existing != null && existing.Key != key)
                return ApiResponse<TenantSettingDto>.ErrorResponse("A setting key cannot be renamed.", 409);
            if (existing != null && existing.IsSensitive && !request.IsSensitive)
                return ApiResponse<TenantSettingDto>.ErrorResponse("Sensitive setting cannot be downgraded without a controlled rotation.", 409);
            if (existing == null && request.IsSensitive && string.IsNullOrWhiteSpace(request.Value))
                return ApiResponse<TenantSettingDto>.ErrorResponse("Sensitive value is required.");
            var row = existing ?? new TenantSetting { TenantId = _user.TenantId, Key = key, CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId };
            var preserveSecret = existing != null && request.IsSensitive &&
                (request.Value == null || request.Value == "********");
            if (!preserveSecret)
            {
                var raw = request.Value ?? string.Empty;
                row.Value = request.IsSensitive ? ProtectedPrefix + _protector.Protect(raw) : raw;
            }
            row.IsSensitive = request.IsSensitive;
            row.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
            if (existing == null) await _settings.AddAsync(row);
            else { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId; }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<TenantSettingDto>.SuccessResponse(Map(row));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<TenantSettingDto>.ErrorResponse("Setting changed. Reload and retry.", 409); }
        catch (DbUpdateException) { return ApiResponse<TenantSettingDto>.ErrorResponse("Setting key conflicts with another update.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Tenant setting save failed for {TenantId}", _user.TenantId);
            return ApiResponse<TenantSettingDto>.ErrorResponse("Setting could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<TenantTerminologyDto>>> GetTerminologyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return ApiResponse<IReadOnlyList<TenantTerminologyDto>>.ErrorResponse("Tenant access is required.", 403);
        var rows = await _terminology.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId)
            .OrderBy(x => x.Key).Take(500).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<TenantTerminologyDto>>.SuccessResponse(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<TenantTerminologyDto>> SaveTerminologyAsync(long? terminologyId,
        SaveTenantTerminologyRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<TenantTerminologyDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.SingularLabel)
            || string.IsNullOrWhiteSpace(request.PluralLabel))
            return ApiResponse<TenantTerminologyDto>.ErrorResponse("Terminology fields are required.");
        try
        {
            var key = request.Key.Trim();
            var query = _terminology.GetQueryable();
            var existing = terminologyId.HasValue
                ? await query.FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.Id == terminologyId.Value, cancellationToken)
                : await query.FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.Key == key, cancellationToken);
            if (terminologyId.HasValue && existing == null)
                return ApiResponse<TenantTerminologyDto>.ErrorResponse("Terminology not found.", 404);
            if (existing != null && terminologyId.HasValue && !MatchesVersion(existing.RowVersion, request.RowVersion))
                return ApiResponse<TenantTerminologyDto>.ErrorResponse("Terminology changed. Reload and retry.", 409);
            if (existing != null && existing.Key != key)
                return ApiResponse<TenantTerminologyDto>.ErrorResponse("Terminology key cannot be renamed.", 409);
            var item = existing ?? new TenantTerminology { TenantId = _user.TenantId, Key = key,
                CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId };
            item.SingularLabel = request.SingularLabel.Trim();
            item.PluralLabel = request.PluralLabel.Trim();
            item.SingularLabelBangla = request.SingularLabelBangla?.Trim();
            item.PluralLabelBangla = request.PluralLabelBangla?.Trim();
            if (existing == null) await _terminology.AddAsync(item);
            else { item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = _user.UserId; }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<TenantTerminologyDto>.SuccessResponse(Map(item));
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<TenantTerminologyDto>.ErrorResponse("Terminology changed. Reload and retry.", 409); }
        catch (DbUpdateException) { return ApiResponse<TenantTerminologyDto>.ErrorResponse("Terminology key conflicts with another update.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Terminology save failed for {TenantId}", _user.TenantId);
            return ApiResponse<TenantTerminologyDto>.ErrorResponse("Terminology could not be saved.", 500);
        }
    }

    private static TenantSettingDto Map(TenantSetting x) => new()
    {
        Id = x.Id, Key = x.Key, Value = x.IsSensitive ? null : x.Value, IsSensitive = x.IsSensitive,
        HasValue = !string.IsNullOrEmpty(x.Value), Category = x.Category,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static TenantTerminologyDto Map(TenantTerminology x) => new()
    {
        Id = x.Id, Key = x.Key, SingularLabel = x.SingularLabel, PluralLabel = x.PluralLabel,
        SingularLabelBangla = x.SingularLabelBangla, PluralLabelBangla = x.PluralLabelBangla,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static bool MatchesVersion(byte[] version, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { return version.AsSpan().SequenceEqual(Convert.FromBase64String(encoded)); }
        catch (FormatException) { return false; }
    }
    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0;
    private bool CanManage() => CanRead() && _user.IsTenantAdmin;
}
