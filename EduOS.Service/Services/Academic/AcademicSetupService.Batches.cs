using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<AcademicBatchDto>>> GetBatchesAsync(
        string? search, long? campusId, long? academicYearId, int page = 1,
        int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<AcademicBatchDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            campusId is <= 0 || academicYearId is <= 0 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicBatchDto>>("Invalid section search, filters or page.");
        var tenant = _currentUser.TenantId;
        var query = _batches.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (campusId.HasValue) query = query.Where(x => x.CampusId == campusId.Value);
        if (academicYearId.HasValue) query = query.Where(x => x.AcademicYearId == academicYearId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = await MapBatchNamesAsync(rows, ct);
        return ApiResponse<PagedResult<AcademicBatchDto>>.SuccessResponse(
            new PagedResult<AcademicBatchDto>
            { Page = page, PageSize = pageSize, TotalCount = total, Items = items });
    }

    public async Task<ApiResponse<AcademicBatchDto>> GetBatchAsync(long id, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<AcademicBatchDto>();
        if (id <= 0) return Error<AcademicBatchDto>("Valid section ID is required.");
        var tenant = _currentUser.TenantId;
        var row = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicBatchDto>("Section not found.", 404);
        var mapped = await MapBatchNamesAsync([row], ct);
        return ApiResponse<AcademicBatchDto>.SuccessResponse(mapped[0]);
    }

    public Task<ApiResponse<AcademicBatchDto>> UpdateBatchAsync(
        long id, UpdateAcademicBatchRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Task.FromResult(Denied<AcademicBatchDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || request.Capacity is < 0 or > 100000 ||
            request.DisplayOrder < 0)
            return Task.FromResult(Error<AcademicBatchDto>("Invalid section name, capacity or order."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<AcademicBatchDto>("Invalid section version.")); }
        if (expected.Length != 8)
            return Task.FromResult(Error<AcademicBatchDto>("Valid section version is required."));
        return ExecuteWriteAsync("update academic section metadata", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _batches.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<AcademicBatchDto>("Section not found.", 404);
            var name = request.Name.Trim();
            // Replayed identical requests do not mutate audit fields or require a new row version.
            if (row.Name == name && row.Capacity == request.Capacity &&
                row.DisplayOrder == request.DisplayOrder)
                return ApiResponse<AcademicBatchDto>.SuccessResponse(MapBatch(row), "Section already has these settings.");
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                return Error<AcademicBatchDto>("Section changed concurrently. Reload before saving.", 409);
            // Without enrollment reconciliation, never reduce capacity or deactivate a live section.
            if (request.Capacity < row.Capacity)
                return Error<AcademicBatchDto>("Capacity reduction requires enrollment reconciliation.", 409);
            row.Name = name;
            row.Capacity = request.Capacity;
            row.DisplayOrder = request.DisplayOrder;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = _currentUser.UserId;
            _batches.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<AcademicBatchDto>.SuccessResponse(MapBatch(row), "Section metadata saved.");
        }, serializable: true);
    }

    private async Task<List<AcademicBatchDto>> MapBatchNamesAsync(
        List<AcademicBatch> rows, CancellationToken ct)
    {
        var tenant = _currentUser.TenantId;
        var campusIds = rows.Select(x => x.CampusId).Distinct().ToArray();
        var yearIds = rows.Select(x => x.AcademicYearId).Distinct().ToArray();
        var programIds = rows.Select(x => x.AcademicProgramId).Distinct().ToArray();
        var levelIds = rows.Select(x => x.AcademicLevelId).Distinct().ToArray();
        var campuses = await _campuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && campusIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var years = await _academicYears.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && yearIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var programs = await _programs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && programIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var levels = await _levels.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && levelIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return rows.Select(row =>
        {
            var dto = MapBatch(row);
            dto.CampusName = campuses.GetValueOrDefault(row.CampusId) ?? string.Empty;
            dto.AcademicYearName = years.GetValueOrDefault(row.AcademicYearId) ?? string.Empty;
            dto.AcademicProgramName = programs.GetValueOrDefault(row.AcademicProgramId) ?? string.Empty;
            dto.AcademicLevelName = levels.GetValueOrDefault(row.AcademicLevelId) ?? string.Empty;
            return dto;
        }).ToList();
    }
}
