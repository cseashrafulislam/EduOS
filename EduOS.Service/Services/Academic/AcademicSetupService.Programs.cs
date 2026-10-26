using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<AcademicProgramDto>>> GetProgramsAsync(
        string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<AcademicProgramDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicProgramDto>>("Invalid programme search or page.");
        var tenant = _currentUser.TenantId;
        var query = _programs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var departmentIds = rows.Where(x => x.AcademicDepartmentId.HasValue)
            .Select(x => x.AcademicDepartmentId!.Value).Distinct().ToArray();
        var names = await _departments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && departmentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var links = await _programCampuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && ids.Contains(x.AcademicProgramId))
            .Select(x => new { x.AcademicProgramId, x.CampusId }).ToListAsync(ct);
        var mapped = rows.Select(row =>
        {
            var dto = MapProgram(row);
            dto.AcademicDepartmentName = row.AcademicDepartmentId.HasValue &&
                names.TryGetValue(row.AcademicDepartmentId.Value, out var name) ? name : null;
            dto.CampusIds = links.Where(x => x.AcademicProgramId == row.Id)
                .Select(x => x.CampusId).OrderBy(x => x).ToArray();
            return dto;
        }).ToArray();
        return ApiResponse<PagedResult<AcademicProgramDto>>.SuccessResponse(
            new PagedResult<AcademicProgramDto>
            {
                Page = page, PageSize = pageSize, TotalCount = count, Items = mapped
            });
    }

    public async Task<ApiResponse<AcademicProgramDto>> GetProgramAsync(
        long id, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<AcademicProgramDto>();
        if (id <= 0) return Error<AcademicProgramDto>("Valid programme ID is required.");
        var tenant = _currentUser.TenantId;
        var row = await _programs.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicProgramDto>("Programme not found.", 404);
        var dto = MapProgram(row);
        if (row.AcademicDepartmentId.HasValue)
            dto.AcademicDepartmentName = await _departments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Id == row.AcademicDepartmentId.Value && !x.IsDeleted)
                .Select(x => x.Name).FirstOrDefaultAsync(ct);
        dto.CampusIds = await _programCampuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AcademicProgramId == id && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.CampusId).Select(x => x.CampusId).ToArrayAsync(ct);
        return ApiResponse<AcademicProgramDto>.SuccessResponse(dto);
    }

    public Task<ApiResponse<AcademicProgramDto>> UpdateProgramAsync(
        long id, UpdateAcademicProgramRequestDto request, CancellationToken ct = default)
    {
        // Department reassignment and programme lifecycle changes are tenant-wide administrative actions.
        if (!CanManageDepartments()) return Task.FromResult(Denied<AcademicProgramDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 200 || request.AcademicDepartmentId is <= 0 ||
            request.ShortName?.Length > 100 || request.AwardTitle?.Length > 150 ||
            request.Description?.Length > 1000 || request.DurationInMonths is < 0 or > 1200 ||
            request.DisplayOrder < 0 || (!request.IsActive && request.IsAdmissionOpen))
            return Task.FromResult(Error<AcademicProgramDto>("Invalid programme metadata or status."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<AcademicProgramDto>("Invalid programme version.")); }
        if (expected.Length != 8)
            return Task.FromResult(Error<AcademicProgramDto>("Valid programme version is required."));
        return ExecuteWriteAsync("update academic programme metadata", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _programs.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<AcademicProgramDto>("Programme not found.", 404);
            var name = request.Name.Trim();
            var shortName = Trim(request.ShortName);
            var awardTitle = Trim(request.AwardTitle);
            var description = Trim(request.Description);
            // Identical retries succeed even when the first write has advanced the rowversion.
            if (row.Name == name && row.AcademicDepartmentId == request.AcademicDepartmentId &&
                row.ShortName == shortName && row.DurationInMonths == request.DurationInMonths &&
                row.AwardTitle == awardTitle && row.Description == description &&
                row.IsAdmissionOpen == request.IsAdmissionOpen && row.IsActive == request.IsActive &&
                row.DisplayOrder == request.DisplayOrder)
                return ApiResponse<AcademicProgramDto>.SuccessResponse(
                    MapProgram(row), "Programme already has these settings.");
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                return Error<AcademicProgramDto>("Programme changed concurrently. Reload before saving.", 409);
            if (request.AcademicDepartmentId.HasValue && !await _departments.GetQueryable().AsNoTracking()
                .AnyAsync(x => x.TenantId == tenant && x.Id == request.AcademicDepartmentId.Value &&
                    x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicProgramDto>("Active academic department not found.", 404);
            var hasBatches = await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicProgramId == id && x.IsActive && !x.IsDeleted, ct);
            var hasCurricula = await _curricula.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicProgramId == id && x.IsActive && !x.IsDeleted, ct);
            // Existing batch schedules and effective-dated curricula must not be silently invalidated.
            if (row.DurationInMonths != request.DurationInMonths && (hasBatches || hasCurricula))
                return Error<AcademicProgramDto>("Duration cannot change while active batches or curricula exist.", 409);
            if (row.IsActive && !request.IsActive)
            {
                var hasLevels = await _levels.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AcademicProgramId == id && x.IsActive && !x.IsDeleted, ct);
                if (hasBatches || hasCurricula || hasLevels)
                    return Error<AcademicProgramDto>("Deactivate linked batches, curricula and levels first.", 409);
            }
            // Code, campus allocations, curriculum versions and credit-system settings are immutable in this workflow.
            row.AcademicDepartmentId = request.AcademicDepartmentId;
            row.Name = name; row.ShortName = shortName;
            row.DurationInMonths = request.DurationInMonths; row.AwardTitle = awardTitle;
            row.Description = description; row.IsAdmissionOpen = request.IsAdmissionOpen;
            row.IsActive = request.IsActive; row.DisplayOrder = request.DisplayOrder;
            row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = _currentUser.UserId;
            _programs.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<AcademicProgramDto>.SuccessResponse(MapProgram(row), "Programme metadata saved.");
        }, serializable: true);
    }
}
