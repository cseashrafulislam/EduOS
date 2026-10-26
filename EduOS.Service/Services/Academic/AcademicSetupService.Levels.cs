using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<AcademicLevelDto>>> GetLevelsAsync(
        string? search, long? academicProgramId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<AcademicLevelDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            academicProgramId is <= 0 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicLevelDto>>("Invalid level search or page.");
        var tenant = _currentUser.TenantId;
        if (academicProgramId.HasValue && !await _programs.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == academicProgramId.Value && !x.IsDeleted, ct))
            return Error<PagedResult<AcademicLevelDto>>("Programme not found.", 404);
        var query = _levels.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (academicProgramId.HasValue)
            query = query.Where(x => x.AcademicProgramId == academicProgramId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim();
            query = query.Where(x => x.Name.StartsWith(prefix) || x.Code.StartsWith(prefix));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.AcademicProgramId).ThenBy(x => x.LevelNo)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await ProgramNamesForLevelsAsync(rows.Select(x => x.AcademicProgramId).Distinct().ToArray(), ct);
        return ApiResponse<PagedResult<AcademicLevelDto>>.SuccessResponse(new PagedResult<AcademicLevelDto>
        {
            Items = rows.Select(x => MapLevelWithProgram(x, names)).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<AcademicLevelDto>> GetLevelAsync(long id, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<AcademicLevelDto>();
        if (id <= 0) return Error<AcademicLevelDto>("Valid level ID is required.");
        var tenant = _currentUser.TenantId;
        var row = await _levels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicLevelDto>("Level not found.", 404);
        var names = await ProgramNamesForLevelsAsync([row.AcademicProgramId], ct);
        return ApiResponse<AcademicLevelDto>.SuccessResponse(MapLevelWithProgram(row, names));
    }

    public Task<ApiResponse<AcademicLevelDto>> UpdateLevelAsync(
        long id, UpdateAcademicLevelRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Task.FromResult(Denied<AcademicLevelDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || request.LevelNo is < 1 or > 1000 ||
            request.DisplayOrder < 0 || (request.IsTerminalLevel && request.IsPromotable))
            return Task.FromResult(Error<AcademicLevelDto>("Invalid level name, number or progression flags."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<AcademicLevelDto>("Invalid level version.")); }
        if (expected.Length != 8) return Task.FromResult(Error<AcademicLevelDto>("Valid level version required."));
        return ExecuteWriteAsync("update academic level", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _levels.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<AcademicLevelDto>("Level not found.", 404);
            var name = request.Name.Trim();
            // Identical retry succeeds even if the previous save advanced RowVersion.
            if (row.Name == name && row.LevelNo == request.LevelNo &&
                row.IsPromotable == request.IsPromotable && row.IsTerminalLevel == request.IsTerminalLevel &&
                row.IsActive == request.IsActive && row.DisplayOrder == request.DisplayOrder)
                return ApiResponse<AcademicLevelDto>.SuccessResponse(MapLevel(row), "Level already updated.");
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                return Error<AcademicLevelDto>("Level changed concurrently. Reload before saving.", 409);
            if (request.IsActive && !await _programs.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == row.AcademicProgramId && x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicLevelDto>("Programme is inactive.", 409);
            if (row.LevelNo != request.LevelNo && await _levels.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicProgramId == row.AcademicProgramId &&
                x.Id != id && x.LevelNo == request.LevelNo && !x.IsDeleted, ct))
                return Error<AcademicLevelDto>("Level number is already in use for this programme.", 409);
            var structural = row.LevelNo != request.LevelNo ||
                row.IsPromotable != request.IsPromotable || row.IsTerminalLevel != request.IsTerminalLevel ||
                (row.IsActive && !request.IsActive);
            if (structural)
            {
                var hasBatches = await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AcademicLevelId == id && !x.IsDeleted, ct);
                var hasCurriculum = await _curriculumSubjects.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AcademicLevelId == id && !x.IsDeleted, ct);
                if (hasBatches || hasCurriculum)
                    return Error<AcademicLevelDto>(
                        "Level progression, number and status are locked by batches or curriculum mappings.", 409);
            }
            // Programme and code remain immutable; existing academic history is retained.
            row.Name = name;
            row.LevelNo = request.LevelNo;
            row.IsPromotable = request.IsPromotable;
            row.IsTerminalLevel = request.IsTerminalLevel;
            row.IsActive = request.IsActive;
            row.DisplayOrder = request.DisplayOrder;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = _currentUser.UserId;
            _levels.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<AcademicLevelDto>.SuccessResponse(MapLevel(row), "Level saved.");
        }, serializable: true);
    }

    private Task<Dictionary<long, string>> ProgramNamesForLevelsAsync(long[] ids, CancellationToken ct) =>
        _programs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _currentUser.TenantId && ids.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private static AcademicLevelDto MapLevelWithProgram(
        EduOS.Core.Entities.Academic.AcademicLevel row, Dictionary<long, string> names)
    {
        var dto = MapLevel(row);
        dto.AcademicProgramName = names.GetValueOrDefault(row.AcademicProgramId) ?? string.Empty;
        return dto;
    }
}
