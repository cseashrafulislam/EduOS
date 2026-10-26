using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<SubjectDto>>> GetSubjectsAsync(
        string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<SubjectDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<SubjectDto>>("Invalid subject search or page.");
        var tenant = _currentUser.TenantId;
        var query = _subjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var prefix = search.Trim();
            query = query.Where(x => x.Name.StartsWith(prefix) || x.Code.StartsWith(prefix));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<SubjectDto>>.SuccessResponse(new PagedResult<SubjectDto>
        {
            Items = rows.Select(MapSubject).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<SubjectDto>> GetSubjectAsync(long id, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<SubjectDto>();
        if (id <= 0) return Error<SubjectDto>("Valid subject ID is required.");
        var tenant = _currentUser.TenantId;
        var row = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        return row == null ? Error<SubjectDto>("Subject not found.", 404)
            : ApiResponse<SubjectDto>.SuccessResponse(MapSubject(row));
    }

    public Task<ApiResponse<SubjectDto>> UpdateSubjectAsync(
        long id, UpdateAcademicSubjectRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Task.FromResult(Denied<SubjectDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 200 || request.ShortName?.Length > 50 ||
            request.DefaultCreditHours is < 0 or > 1000)
            return Task.FromResult(Error<SubjectDto>("Invalid subject metadata."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<SubjectDto>("Invalid subject version.")); }
        if (expected.Length != 8) return Task.FromResult(Error<SubjectDto>("Valid subject version required."));
        return ExecuteWriteAsync("update academic subject", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _subjects.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<SubjectDto>("Subject not found.", 404);
            var name = request.Name.Trim();
            var shortName = Trim(request.ShortName);
            // Identical retries are safe even if the first write advanced the row version.
            if (row.Name == name && row.ShortName == shortName &&
                row.DefaultCreditHours == request.DefaultCreditHours && row.IsActive == request.IsActive)
                return ApiResponse<SubjectDto>.SuccessResponse(MapSubject(row), "Subject already updated.");
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                return Error<SubjectDto>("Subject changed concurrently. Reload before saving.", 409);
            if (row.IsActive && !request.IsActive && await _curriculumSubjects.GetQueryable()
                .AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.SubjectId == id && !x.IsDeleted, ct))
                return Error<SubjectDto>(
                    "Subject cannot be deactivated while curriculum history references it.", 409);
            // Subject code and existing curriculum marks/credits remain immutable here.
            row.Name = name;
            row.ShortName = shortName;
            row.DefaultCreditHours = request.DefaultCreditHours;
            row.IsActive = request.IsActive;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = _currentUser.UserId;
            _subjects.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<SubjectDto>.SuccessResponse(MapSubject(row), "Subject saved.");
        }, serializable: true);
    }
}
