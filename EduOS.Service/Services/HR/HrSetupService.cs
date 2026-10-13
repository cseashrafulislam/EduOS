using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.HR;

/// <summary>Institution-wide HR masters only. Campus-owned organization units are never modified here.</summary>
public sealed class HrSetupService : IHrSetupService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public HrSetupService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool Readable => user.IsAuthenticated && user.TenantId > 0 &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("HR"));
    // No campus-scoped permission claim is available here; tenant-wide writes are admin-only.
    private bool Writable => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static bool Matches(byte[] actual, string? supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private IQueryable<OrganizationUnit> Units => db.OrganizationUnits.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<Designation> Designations => db.Designations.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<WorkShift> Shifts => db.WorkShifts.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static OrganizationUnitDto UnitDto(OrganizationUnit x, string? parent = null) => new()
    { Id = x.Id, ParentId = x.ParentId, ParentName = parent, Name = x.Name, Code = x.Code, IsActive = x.IsActive, RowVersion = Version(x.RowVersion) };
    private static DesignationDto DesignationDto(Designation x) => new()
    { Id = x.Id, Name = x.Name, Code = x.Code, Rank = x.Rank, IsActive = x.IsActive, RowVersion = Version(x.RowVersion) };
    private static WorkShiftDto ShiftDto(WorkShift x) => new()
    { Id = x.Id, Name = x.Name, Code = x.Code, StartTime = x.StartTime, EndTime = x.EndTime, GraceMinutes = x.GraceMinutes, IsActive = x.IsActive, RowVersion = Version(x.RowVersion) };
    private static bool Valid(string? name, int nameLength, string? code) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= nameLength &&
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 50;
    private static string Code(string value) => value.Trim().ToUpperInvariant();

    public async Task<ApiResponse<PagedResult<OrganizationUnitDto>>> GetOrganizationUnitsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!Readable) return Error<PagedResult<OrganizationUnitDto>>("HR access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 100) return Error<PagedResult<OrganizationUnitDto>>("Invalid page.");
        var query = Units.AsNoTracking().Where(x => x.CampusId == null);
        var count = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue) return Error<PagedResult<OrganizationUnitDto>>("Page out of range.");
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        var parentIds = rows.Where(x => x.ParentId.HasValue).Select(x => x.ParentId!.Value).Distinct().ToArray();
        var parents = await Units.AsNoTracking().Where(x => x.CampusId == null && parentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return ApiResponse<PagedResult<OrganizationUnitDto>>.SuccessResponse(new PagedResult<OrganizationUnitDto>
        {
            Page = page, PageSize = pageSize, TotalCount = count,
            Items = rows.Select(x => UnitDto(x, x.ParentId.HasValue && parents.TryGetValue(x.ParentId.Value, out var name) ? name : null)).ToList()
        });
    }

    public async Task<ApiResponse<OrganizationUnitDto>> SaveOrganizationUnitAsync(long? id, SaveOrganizationUnitRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Error<OrganizationUnitDto>("Only the institution administrator can change shared HR masters.", 403);
        if (request == null || !Valid(request.Name, 150, request.Code) || request.ParentId is <= 0 || id is <= 0)
            return Error<OrganizationUnitDto>("Invalid department details.");
        var code = Code(request.Code); var name = request.Name.Trim();
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var row = id.HasValue ? await Units.FirstOrDefaultAsync(x => x.Id == id.Value && x.CampusId == null, token) : null;
                if (id.HasValue && row == null) return Error<OrganizationUnitDto>("Department not found or campus-owned.", 404);
                if (row != null && !Matches(row.RowVersion, request.RowVersion)) return Error<OrganizationUnitDto>("Department changed; reload.", 409);
                if (await Units.AnyAsync(x => x.Code == code && (!id.HasValue || x.Id != id.Value), token))
                    return Error<OrganizationUnitDto>("Department code already exists.", 409);
                if (request.ParentId.HasValue)
                {
                    var currentId = request.ParentId;
                    var seen = new HashSet<long>();
                    while (currentId.HasValue)
                    {
                        if (!seen.Add(currentId.Value) || id == currentId)
                            return Error<OrganizationUnitDto>("Department hierarchy cycle detected.", 409);
                        var parent = await Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == currentId.Value && x.CampusId == null, token);
                        if (parent == null || !parent.IsActive) return Error<OrganizationUnitDto>("Parent must be an active institution-wide department.", 409);
                        currentId = parent.ParentId;
                    }
                }
                if (row != null && !request.IsActive)
                {
                    if (await Units.AnyAsync(x => x.ParentId == row.Id && x.IsActive, token))
                        return Error<OrganizationUnitDto>("Deactivate child departments first.", 409);
                    if (await db.Employees.AnyAsync(x => x.TenantId == user.TenantId && !x.IsDeleted &&
                        x.OrganizationUnitId == row.Id && (x.State == EmployeeState.Active || x.State == EmployeeState.Suspended), token))
                        return Error<OrganizationUnitDto>("Department has active or suspended employees.", 409);
                }
                var now = clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new OrganizationUnit { TenantId = user.TenantId, CampusId = null, CreatedAt = now, CreatedBy = user.UserId };
                    db.OrganizationUnits.Add(row);
                }
                else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
                row.Name = name; row.Code = code; row.ParentId = request.ParentId; row.IsActive = request.IsActive;
                await db.SaveChangesAsync(token);
                return ApiResponse<OrganizationUnitDto>.SuccessResponse(UnitDto(row), "Department saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<OrganizationUnitDto>("Department changed; reload.", 409); }
        catch (DbUpdateException) { return Error<OrganizationUnitDto>("Department conflicts with existing data.", 409); }
    }

    public async Task<ApiResponse<IReadOnlyList<DesignationDto>>> GetDesignationsAsync(CancellationToken ct = default)
    {
        if (!Readable) return Error<IReadOnlyList<DesignationDto>>("HR access denied.", 403);
        var rows = await Designations.AsNoTracking().OrderBy(x => x.Rank).ThenBy(x => x.Name).Take(1001).ToListAsync(ct);
        if (rows.Count > 1000) return Error<IReadOnlyList<DesignationDto>>("Too many designations; contact administrator.", 409);
        return ApiResponse<IReadOnlyList<DesignationDto>>.SuccessResponse(rows.Select(DesignationDto).ToArray());
    }

    public async Task<ApiResponse<DesignationDto>> SaveDesignationAsync(long? id, SaveDesignationRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Error<DesignationDto>("Only the institution administrator can change shared HR masters.", 403);
        if (request == null || !Valid(request.Name, 150, request.Code) || request.Rank is < 0 or > 100000 || id is <= 0)
            return Error<DesignationDto>("Invalid designation details.");
        var code = Code(request.Code); var name = request.Name.Trim();
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var row = id.HasValue ? await Designations.FirstOrDefaultAsync(x => x.Id == id.Value, token) : null;
                if (id.HasValue && row == null) return Error<DesignationDto>("Designation not found.", 404);
                if (row != null && !Matches(row.RowVersion, request.RowVersion)) return Error<DesignationDto>("Designation changed; reload.", 409);
                if (await Designations.AnyAsync(x => x.Code == code && (!id.HasValue || x.Id != id.Value), token))
                    return Error<DesignationDto>("Designation code already exists.", 409);
                if (row != null && !request.IsActive && await db.Employees.AnyAsync(x => x.TenantId == user.TenantId &&
                    !x.IsDeleted && x.DesignationId == row.Id && (x.State == EmployeeState.Active || x.State == EmployeeState.Suspended), token))
                    return Error<DesignationDto>("Designation is assigned to active or suspended employees.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                if (row == null) { row = new Designation { TenantId = user.TenantId, CreatedAt = now, CreatedBy = user.UserId }; db.Designations.Add(row); }
                else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
                row.Name = name; row.Code = code; row.Rank = request.Rank; row.IsActive = request.IsActive;
                await db.SaveChangesAsync(token);
                return ApiResponse<DesignationDto>.SuccessResponse(DesignationDto(row), "Designation saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<DesignationDto>("Designation changed; reload.", 409); }
        catch (DbUpdateException) { return Error<DesignationDto>("Designation conflicts with existing data.", 409); }
    }

    public async Task<ApiResponse<IReadOnlyList<WorkShiftDto>>> GetWorkShiftsAsync(CancellationToken ct = default)
    {
        if (!Readable) return Error<IReadOnlyList<WorkShiftDto>>("HR access denied.", 403);
        var rows = await Shifts.AsNoTracking().OrderBy(x => x.StartTime).ThenBy(x => x.Name).Take(1001).ToListAsync(ct);
        if (rows.Count > 1000) return Error<IReadOnlyList<WorkShiftDto>>("Too many work shifts; contact administrator.", 409);
        return ApiResponse<IReadOnlyList<WorkShiftDto>>.SuccessResponse(rows.Select(ShiftDto).ToArray());
    }

    public async Task<ApiResponse<WorkShiftDto>> SaveWorkShiftAsync(long? id, SaveWorkShiftRequestDto request, CancellationToken ct = default)
    {
        if (!Writable) return Error<WorkShiftDto>("Only the institution administrator can change shared HR masters.", 403);
        if (request == null || !Valid(request.Name, 100, request.Code) || request.GraceMinutes is < 0 or > 240 ||
            request.StartTime == request.EndTime || id is <= 0)
            return Error<WorkShiftDto>("Invalid shift details or equal start/end times.");
        var code = Code(request.Code); var name = request.Name.Trim();
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var row = id.HasValue ? await Shifts.FirstOrDefaultAsync(x => x.Id == id.Value, token) : null;
                if (id.HasValue && row == null) return Error<WorkShiftDto>("Shift not found.", 404);
                if (row != null && !Matches(row.RowVersion, request.RowVersion)) return Error<WorkShiftDto>("Shift changed; reload.", 409);
                if (await Shifts.AnyAsync(x => x.Code == code && (!id.HasValue || x.Id != id.Value), token))
                    return Error<WorkShiftDto>("Shift code already exists.", 409);
                if (row != null && !request.IsActive && await db.EmployeeShiftAssignments.AnyAsync(x => x.TenantId == user.TenantId &&
                    !x.IsDeleted && x.WorkShiftId == row.Id && x.IsCurrent, token))
                    return Error<WorkShiftDto>("Shift is assigned to employees; end assignments first.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                if (row == null) { row = new WorkShift { TenantId = user.TenantId, CreatedAt = now, CreatedBy = user.UserId }; db.WorkShifts.Add(row); }
                else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
                row.Name = name; row.Code = code; row.StartTime = request.StartTime; row.EndTime = request.EndTime;
                row.GraceMinutes = request.GraceMinutes; row.IsActive = request.IsActive;
                await db.SaveChangesAsync(token);
                return ApiResponse<WorkShiftDto>.SuccessResponse(ShiftDto(row), "Shift saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<WorkShiftDto>("Shift changed; reload.", 409); }
        catch (DbUpdateException) { return Error<WorkShiftDto>("Shift conflicts with existing data.", 409); }
    }
}
