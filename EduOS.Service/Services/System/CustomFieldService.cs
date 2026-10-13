using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EduOS.Service.Services.Platform;

// Shared Platform owns tenant-profile fields only. Module records must be managed by their owners.
public sealed class CustomFieldService : ICustomFieldService
{
    private readonly IGenericRepository<CustomFieldDefinition> _definitions;
    private readonly IGenericRepository<CustomFieldOption> _options;
    private readonly IGenericRepository<CustomFieldValue> _values;
    private readonly IUnitOfWork _unit;
    private readonly ICurrentUserService _user;
    private readonly ILogger<CustomFieldService> _logger;
    public CustomFieldService(IGenericRepository<CustomFieldDefinition> definitions,
        IGenericRepository<CustomFieldOption> options, IGenericRepository<CustomFieldValue> values,
        IUnitOfWork unit, ICurrentUserService user, ILogger<CustomFieldService> logger)
    { _definitions = definitions; _options = options; _values = values; _unit = unit; _user = user; _logger = logger; }

    private bool Allowed => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private bool Owns(string type, long id) => Allowed && id == _user.TenantId &&
        string.Equals(type, "Tenant", StringComparison.OrdinalIgnoreCase);
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Tenant administrator access required.", 403);
    private static bool Version(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static bool Supported(CustomFieldDataType t) => t is CustomFieldDataType.Text or CustomFieldDataType.Number or
        CustomFieldDataType.Decimal or CustomFieldDataType.Date or CustomFieldDataType.Boolean or CustomFieldDataType.Select;
    private static CustomFieldDefinitionDto Map(CustomFieldDefinition d, IEnumerable<CustomFieldOption> options) => new()
    {
        Id = d.Id, EntityType = d.EntityType, FieldKey = d.FieldKey, Label = d.Label, DataType = d.DataType,
        IsRequired = d.IsRequired, IsSensitive = d.IsSensitive, IsActive = d.IsActive, DisplayOrder = d.DisplayOrder,
        ValidationJson = d.ValidationJson, RowVersion = Convert.ToBase64String(d.RowVersion),
        Options = options.OrderBy(x => x.DisplayOrder).Select(x => new CustomFieldOptionDto
        { Id = x.Id, Value = x.Value, Label = x.Label, DisplayOrder = x.DisplayOrder, IsActive = x.IsActive }).ToArray()
    };

    public async Task<ApiResponse<PagedResult<CustomFieldDefinitionDto>>> GetDefinitionsAsync(string? entityType, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed || (entityType != null && !string.Equals(entityType, "Tenant", StringComparison.OrdinalIgnoreCase)))
            return Denied<PagedResult<CustomFieldDefinitionDto>>();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _definitions.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.EntityType == "Tenant" && !x.IsSensitive && !x.IsDeleted);
        var count = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        var rows = offset > int.MaxValue ? new List<CustomFieldDefinition>() :
            await query.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var options = await _options.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            ids.Contains(x.CustomFieldDefinitionId) && !x.IsDeleted).ToListAsync(ct);
        return ApiResponse<PagedResult<CustomFieldDefinitionDto>>.SuccessResponse(new PagedResult<CustomFieldDefinitionDto>
        { Items = rows.Select(x => Map(x, options.Where(o => o.CustomFieldDefinitionId == x.Id))).ToList(),
          TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<CustomFieldDefinitionDto>> SaveDefinitionAsync(long? definitionId,
        SaveCustomFieldDefinitionRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Denied<CustomFieldDefinitionDto>();
        if (request == null || request.IsSensitive || !string.IsNullOrWhiteSpace(request.ValidationJson) ||
            !string.Equals(request.EntityType, "Tenant", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(request.FieldKey?.Trim() ?? "", "^[A-Za-z][A-Za-z0-9_]{0,99}$") ||
            string.IsNullOrWhiteSpace(request.Label) || request.Label.Trim().Length > 150 ||
            !Supported(request.DataType) || request.Options == null || request.Options.Count > 50)
            return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Invalid tenant field definition.");
        var key = request.FieldKey?.Trim() ?? string.Empty;
        var choices = request.Options.Select(x => new { Value = x.Value?.Trim(), Label = x.Label?.Trim(),
            x.DisplayOrder, x.IsActive }).ToArray();
        if ((request.DataType != CustomFieldDataType.Select && choices.Length != 0) ||
            (request.DataType == CustomFieldDataType.Select && !choices.Any(x => x.IsActive)) ||
            choices.Any(x => string.IsNullOrWhiteSpace(x.Value) || x.Value.Length > 100 ||
                string.IsNullOrWhiteSpace(x.Label) || x.Label.Length > 150) ||
            choices.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != choices.Length)
            return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Select choices are missing or duplicated.");
        try
        {
            return await _unit.ExecuteInTransactionAsync(async token =>
            {
                var query = _definitions.GetQueryable().Where(x => x.TenantId == _user.TenantId && !x.IsDeleted);
                var existing = definitionId.HasValue
                    ? await query.FirstOrDefaultAsync(x => x.Id == definitionId.Value, token)
                    : await query.FirstOrDefaultAsync(x => x.EntityType == "Tenant" && x.FieldKey == key, token);
                if (definitionId.HasValue && existing == null)
                    return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Field not found.", 404);
                if (existing?.IsSensitive == true)
                    return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Sensitive fields require a separate protected workflow.", 403);
                if (!definitionId.HasValue && existing != null)
                    return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Field key already exists.", 409);
                if (existing != null && !Version(existing.RowVersion, request.RowVersion))
                    return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Field changed. Reload and retry.", 409);
                if (existing != null && (existing.EntityType != "Tenant" || existing.FieldKey != key ||
                    existing.DataType != request.DataType))
                    return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Field identity and type cannot be changed.", 409);
                if (existing == null && await _definitions.GetQueryable().CountAsync(x => x.TenantId == _user.TenantId && x.EntityType == "Tenant" && !x.IsSensitive && !x.IsDeleted, token) >= 200)
                    return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Maximum 200 institution custom fields are allowed.", 409);
                var row = existing ?? new CustomFieldDefinition
                { TenantId = _user.TenantId, EntityType = "Tenant", FieldKey = key, DataType = request.DataType,
                  CreatedBy = _user.UserId, CreatedAt = DateTime.UtcNow };
                var old = existing == null ? new List<CustomFieldOption>() :
                    await _options.GetQueryable().Where(x => x.TenantId == _user.TenantId &&
                        x.CustomFieldDefinitionId == row.Id && !x.IsDeleted).ToListAsync(token);
                if (existing != null && request.DataType == CustomFieldDataType.Select)
                {
                    var retired = old.Where(o => o.IsActive && !choices.Any(c => c.IsActive &&
                        string.Equals(c.Value, o.Value, StringComparison.OrdinalIgnoreCase)))
                        .Select(o => o.Value).ToArray();
                    if (retired.Length > 0 && await _values.GetQueryable().AnyAsync(x =>
                        x.TenantId == _user.TenantId && x.CustomFieldDefinitionId == row.Id &&
                        !x.IsDeleted && x.Value != null && retired.Contains(x.Value), token))
                        return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("A selected choice is in use.", 409);
                }
                if (existing == null) { await _definitions.AddAsync(row); await _unit.SaveChangesAsync(token); }
                row.Label = request.Label.Trim(); row.IsRequired = request.IsRequired;
                row.IsActive = request.IsActive; row.DisplayOrder = request.DisplayOrder;
                if (existing != null) { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _user.UserId; }
                foreach (var opt in old.Where(o => !choices.Any(c => string.Equals(c.Value, o.Value, StringComparison.OrdinalIgnoreCase))))
                { opt.IsActive = false; opt.UpdatedAt = DateTime.UtcNow; opt.UpdatedBy = _user.UserId; }
                foreach (var c in choices)
                {
                    var opt = old.FirstOrDefault(o => string.Equals(o.Value, c.Value, StringComparison.OrdinalIgnoreCase));
                    if (opt == null)
                    {
                        opt = new CustomFieldOption { TenantId = _user.TenantId, CustomFieldDefinitionId = row.Id,
                            Value = c.Value!, CreatedBy = _user.UserId, CreatedAt = DateTime.UtcNow };
                        await _options.AddAsync(opt);
                    }
                    else { opt.UpdatedAt = DateTime.UtcNow; opt.UpdatedBy = _user.UserId; }
                    opt.Label = c.Label!; opt.IsActive = c.IsActive; opt.DisplayOrder = c.DisplayOrder;
                }
                await _unit.SaveChangesAsync(token);
                var saved = await _options.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
                    x.CustomFieldDefinitionId == row.Id && !x.IsDeleted).ToListAsync(token);
                return ApiResponse<CustomFieldDefinitionDto>.SuccessResponse(Map(row, saved));
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Field changed. Reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Concurrent field update. Reload.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger.LogError(ex, "Tenant custom field save failed for {TenantId}", _user.TenantId);
          return ApiResponse<CustomFieldDefinitionDto>.ErrorResponse("Field could not be saved.", 500); }
    }

    public async Task<ApiResponse<IReadOnlyList<CustomFieldValueDto>>> GetValuesAsync(string entityType, long entityId, CancellationToken ct = default)
    {
        if (!Owns(entityType, entityId)) return Denied<IReadOnlyList<CustomFieldValueDto>>();
        var defs = await _definitions.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.EntityType == "Tenant" && x.IsActive && !x.IsSensitive && !x.IsDeleted)
            .OrderBy(x => x.DisplayOrder).Take(200).ToListAsync(ct);
        var ids = defs.Select(x => x.Id).ToArray();
        var values = await _values.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.EntityId == entityId && ids.Contains(x.CustomFieldDefinitionId) && !x.IsDeleted).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.SuccessResponse(defs.Select(d =>
        {
            var v = values.FirstOrDefault(x => x.CustomFieldDefinitionId == d.Id);
            return new CustomFieldValueDto { CustomFieldDefinitionId = d.Id, FieldKey = d.FieldKey, Label = d.Label,
                Value = v?.Value, RowVersion = v == null ? null : Convert.ToBase64String(v.RowVersion) };
        }).ToArray());
    }

    public async Task<ApiResponse<IReadOnlyList<CustomFieldValueDto>>> SaveValuesAsync(string entityType, long entityId,
        IReadOnlyList<SaveCustomFieldValueRequestDto> requests, CancellationToken ct = default)
    {
        if (!Owns(entityType, entityId)) return Denied<IReadOnlyList<CustomFieldValueDto>>();
        if (requests == null || requests.Count > 200 || requests.Any(x => x == null) ||
            requests.Select(x => x.CustomFieldDefinitionId).Distinct().Count() != requests.Count)
            return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse("Invalid or duplicated values.");
        try
        {
            return await _unit.ExecuteInTransactionAsync(async token =>
            {
                var defs = await _definitions.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
                    x.EntityType == "Tenant" && x.IsActive && !x.IsSensitive && !x.IsDeleted).ToListAsync(token);
                if (defs.Count > 200 || requests.Any(r => defs.All(d => d.Id != r.CustomFieldDefinitionId)))
                    return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse("Field not available.");
                var ids = defs.Select(d => d.Id).ToArray();
                var old = await _values.GetQueryable().Where(x => x.TenantId == _user.TenantId &&
                    x.EntityId == entityId && ids.Contains(x.CustomFieldDefinitionId) && !x.IsDeleted).ToListAsync(token);
                var options = await _options.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
                    ids.Contains(x.CustomFieldDefinitionId) && x.IsActive && !x.IsDeleted).ToListAsync(token);
                // Validate all inputs before mutating tracked values; explicit null is a clear, not an omission.
                foreach (var d in defs)
                {
                    var input = requests.FirstOrDefault(r => r.CustomFieldDefinitionId == d.Id);
                    var current = old.FirstOrDefault(v => v.CustomFieldDefinitionId == d.Id);
                    var value = input == null ? current?.Value : input.Value?.Trim();
                    if (d.IsRequired && string.IsNullOrWhiteSpace(value))
                        return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse(d.Label + " is required.");
                    if (input == null) continue;
                    if (!Valid(d, value, options))
                        return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse(d.Label + " has an invalid value.");
                    if (current != null && current.Value != value && !Version(current.RowVersion, input.RowVersion))
                        return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse("Values changed. Reload.", 409);
                }
                foreach (var input in requests)
                {
                    var current = old.FirstOrDefault(v => v.CustomFieldDefinitionId == input.CustomFieldDefinitionId);
                    var value = input.Value?.Trim();
                    if (current != null)
                    {
                        if (current.Value == value) continue;
                        current.Value = value; current.UpdatedAt = DateTime.UtcNow; current.UpdatedBy = _user.UserId;
                    }
                    else if (!string.IsNullOrWhiteSpace(value))
                        await _values.AddAsync(new CustomFieldValue { TenantId = _user.TenantId, EntityId = entityId,
                            CustomFieldDefinitionId = input.CustomFieldDefinitionId, Value = value,
                            CreatedAt = DateTime.UtcNow, CreatedBy = _user.UserId });
                }
                await _unit.SaveChangesAsync(token);
                return await GetValuesAsync("Tenant", entityId, token);
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse("Values changed. Reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse("Concurrent update. Reload.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger.LogError(ex, "Tenant custom values save failed for {TenantId}", _user.TenantId);
          return ApiResponse<IReadOnlyList<CustomFieldValueDto>>.ErrorResponse("Values could not be saved.", 500); }
    }

    private static bool Valid(CustomFieldDefinition d, string? v, List<CustomFieldOption> options)
    {
        if (string.IsNullOrEmpty(v)) return !d.IsRequired;
        if (v.Length > 4000) return false;
        return d.DataType switch
        {
            CustomFieldDataType.Text => true,
            CustomFieldDataType.Number => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            CustomFieldDataType.Decimal => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            CustomFieldDataType.Date => DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            CustomFieldDataType.Boolean => v is "true" or "false",
            CustomFieldDataType.Select => options.Any(o => o.CustomFieldDefinitionId == d.Id && o.Value == v),
            _ => false
        };
    }
}
