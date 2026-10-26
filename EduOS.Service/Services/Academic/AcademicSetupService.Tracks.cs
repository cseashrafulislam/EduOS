using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<AcademicTrackDto>>> GetTracksAsync(
        string? search, long? academicProgramId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<AcademicTrackDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            academicProgramId is <= 0 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicTrackDto>>("Invalid track search or page.");
        var tenant = _currentUser.TenantId;
        if (academicProgramId.HasValue && !await _programs.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == academicProgramId.Value && !x.IsDeleted, ct))
            return Error<PagedResult<AcademicTrackDto>>("Programme not found.", 404);
        var query = _tracks.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (academicProgramId.HasValue)
            query = query.Where(x => x.AcademicProgramId == academicProgramId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim();
            query = query.Where(x => x.Name.StartsWith(prefix) || x.Code.StartsWith(prefix));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.AcademicProgramId).ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await TrackProgramNamesAsync(rows.Where(x => x.AcademicProgramId.HasValue)
            .Select(x => x.AcademicProgramId!.Value).Distinct().ToArray(), ct);
        return ApiResponse<PagedResult<AcademicTrackDto>>.SuccessResponse(new PagedResult<AcademicTrackDto>
        {
            Items = rows.Select(x => MapTrackWithProgram(x, names)).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<AcademicTrackDto>> GetTrackAsync(long id, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<AcademicTrackDto>();
        if (id <= 0) return Error<AcademicTrackDto>("Valid track ID is required.");
        var tenant = _currentUser.TenantId;
        var row = await _tracks.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicTrackDto>("Track not found.", 404);
        var names = await TrackProgramNamesAsync(row.AcademicProgramId.HasValue
            ? [row.AcademicProgramId.Value] : [], ct);
        return ApiResponse<AcademicTrackDto>.SuccessResponse(MapTrackWithProgram(row, names));
    }

    public Task<ApiResponse<AcademicTrackDto>> UpdateTrackAsync(
        long id, UpdateAcademicTrackRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Task.FromResult(Denied<AcademicTrackDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || request.Description?.Length > 500 ||
            request.DisplayOrder < 0)
            return Task.FromResult(Error<AcademicTrackDto>("Invalid track metadata."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<AcademicTrackDto>("Invalid track version.")); }
        if (expected.Length != 8) return Task.FromResult(Error<AcademicTrackDto>("Valid track version required."));
        return ExecuteWriteAsync("update academic track", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _tracks.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<AcademicTrackDto>("Track not found.", 404);
            var name = request.Name.Trim();
            var description = Trim(request.Description);
            // Replayed identical PUTs are safe even if the first save advanced RowVersion.
            if (row.Name == name && row.Description == description &&
                row.IsDefault == request.IsDefault && row.IsActive == request.IsActive &&
                row.DisplayOrder == request.DisplayOrder)
                return ApiResponse<AcademicTrackDto>.SuccessResponse(MapTrack(row), "Track already updated.");
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                return Error<AcademicTrackDto>("Track changed concurrently. Reload before saving.", 409);
            if (request.IsActive && row.AcademicProgramId.HasValue &&
                !await _programs.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == row.AcademicProgramId.Value &&
                    x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicTrackDto>("Programme is inactive.", 409);
            if (request.IsDefault && request.IsActive && await _tracks.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id != id && !x.IsDeleted &&
                x.AcademicProgramId == row.AcademicProgramId && x.IsDefault && x.IsActive, ct))
                return Error<AcademicTrackDto>("Another active default track exists for this programme.", 409);
            if (row.IsActive && !request.IsActive)
            {
                var batches = await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AcademicTrackId == id && !x.IsDeleted, ct);
                var curricula = await _curricula.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AcademicTrackId == id && !x.IsDeleted, ct);
                if (batches || curricula)
                    return Error<AcademicTrackDto>(
                        "Track cannot be deactivated while academic batches or curriculum history reference it.", 409);
            }
            // Code and programme scope are deliberately immutable; historical references remain intact.
            row.Name = name;
            row.Description = description;
            row.IsDefault = request.IsDefault;
            row.IsActive = request.IsActive;
            row.DisplayOrder = request.DisplayOrder;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = _currentUser.UserId;
            _tracks.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<AcademicTrackDto>.SuccessResponse(MapTrack(row), "Track saved.");
        }, serializable: true);
    }

    private Task<Dictionary<long, string>> TrackProgramNamesAsync(long[] ids, CancellationToken ct) =>
        _programs.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _currentUser.TenantId && ids.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private static AcademicTrackDto MapTrackWithProgram(AcademicTrack row, Dictionary<long, string> names)
    {
        var dto = MapTrack(row);
        dto.AcademicProgramName = row.AcademicProgramId.HasValue
            ? names.GetValueOrDefault(row.AcademicProgramId.Value) : null;
        return dto;
    }
}
