using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    private bool CanManageDepartments() => _currentUser.IsAuthenticated &&
        _currentUser.TenantId > 0 && _currentUser.IsTenantAdmin;

    public async Task<ApiResponse<PagedResult<AcademicDepartmentDto>>> GetDepartmentsAsync(
        string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<AcademicDepartmentDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<AcademicDepartmentDto>>("Invalid department search or page.");
        var tenant = _currentUser.TenantId;
        var query = _departments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Code.StartsWith(term) || x.Name.StartsWith(term));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<AcademicDepartmentDto>>.SuccessResponse(
            new PagedResult<AcademicDepartmentDto>
            {
                Page = page, PageSize = pageSize, TotalCount = count,
                Items = rows.Select(MapDepartment).ToList()
            });
    }

    public Task<ApiResponse<AcademicDepartmentDto>> CreateDepartmentAsync(
        SaveAcademicDepartmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Task.FromResult(Denied<AcademicDepartmentDto>());
        if (!ValidDepartment(request))
            return Task.FromResult(Error<AcademicDepartmentDto>("Valid department name, code and description are required."));
        return ExecuteWriteAsync("create academic department", async () =>
        {
            var tenant = _currentUser.TenantId;
            var code = NormalizeCode(request.Code);
            var name = request.Name.Trim();
            var description = Trim(request.Description);
            var existing = await _departments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Code == code && !x.IsDeleted, ct);
            if (existing != null)
            {
                if (existing.Name != name || existing.Description != description ||
                    existing.IsActive != request.IsActive)
                    return Error<AcademicDepartmentDto>("Department code already exists with different settings.", 409);
                return ApiResponse<AcademicDepartmentDto>.SuccessResponse(
                    MapDepartment(existing), "Department already exists.");
            }
            var row = new AcademicDepartment
            {
                TenantId = tenant, Code = code, Name = name, Description = description,
                IsActive = request.IsActive, CreatedBy = _currentUser.UserId
            };
            await _departments.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return Created(MapDepartment(row), "Academic department created.");
        }, serializable: true);
    }

    public Task<ApiResponse<AcademicDepartmentDto>> UpdateDepartmentAsync(
        long id, SaveAcademicDepartmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Task.FromResult(Denied<AcademicDepartmentDto>());
        if (id <= 0 || !ValidDepartment(request))
            return Task.FromResult(Error<AcademicDepartmentDto>("Valid department and settings are required."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<AcademicDepartmentDto>("Invalid department version.")); }
        if (expected.Length != 8)
            return Task.FromResult(Error<AcademicDepartmentDto>("Valid department version is required."));
        return ExecuteWriteAsync("update academic department", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _departments.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<AcademicDepartmentDto>("Department not found.", 404);
            var name = request.Name.Trim();
            var description = Trim(request.Description);
            if (row.Code != NormalizeCode(request.Code))
                return Error<AcademicDepartmentDto>("Department code cannot be changed.", 409);
            if (row.Name == name && row.Description == description && row.IsActive == request.IsActive)
                return ApiResponse<AcademicDepartmentDto>.SuccessResponse(
                    MapDepartment(row), "Department already has these settings.");
            if (!row.RowVersion.SequenceEqual(expected))
                return Error<AcademicDepartmentDto>("Department changed concurrently. Reload and retry.", 409);
            if (!request.IsActive && await _programs.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicDepartmentId == id && x.IsActive && !x.IsDeleted, ct))
                return Error<AcademicDepartmentDto>("Active programmes must be reassigned or deactivated first.", 409);
            row.Name = name;
            row.Description = description;
            row.IsActive = request.IsActive;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = _currentUser.UserId;
            _departments.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<AcademicDepartmentDto>.SuccessResponse(
                MapDepartment(row), "Academic department updated.");
        }, serializable: true);
    }

    private static bool ValidDepartment(SaveAcademicDepartmentRequestDto? request) =>
        request != null && !string.IsNullOrWhiteSpace(request.Name) &&
        request.Name.Trim().Length <= 150 && !string.IsNullOrWhiteSpace(request.Code) &&
        request.Code.Trim().Length <= 30 &&
        (request.Description == null || request.Description.Length <= 500);

    private static AcademicDepartmentDto MapDepartment(AcademicDepartment row) => new()
    {
        Id = row.Id, Name = row.Name, Code = row.Code, Description = row.Description,
        IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
}
