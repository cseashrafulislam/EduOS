using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.HR;

/// <summary>Institution-wide leave policy and entitlements. Campus-scoped HR actors are not authorized.</summary>
public sealed class LeaveEntitlementService : ILeaveEntitlementService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public LeaveEntitlementService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool Authorized => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private IQueryable<LeaveType> Types => db.LeaveTypes.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<Employee> Employees => db.Employees.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<EmployeeLeaveEntitlement> Entitlements => db.EmployeeLeaveEntitlements.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<EmployeeLeaveAdjustment> Adjustments => db.EmployeeLeaveAdjustments.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<EmployeeLeaveApplication> Applications => db.EmployeeLeaveApplications.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static bool Matches(byte[] actual, string? value)
    {
        try { return !string.IsNullOrWhiteSpace(value) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(value)); }
        catch (FormatException) { return false; }
    }
    private static bool MoneyScale(decimal value) => decimal.Round(value, 2) == value;
    private bool ValidYear(int year) => year >= 2000 && year <= clock.GetUtcNow().Year + 1;
    private static LeaveTypeDto Map(LeaveType row) => new()
    {
        Id = row.Id, Name = row.Name, Code = row.Code, MaxDaysPerYear = row.MaxDaysPerYear,
        IsPaid = row.IsPaid, IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
    private static EmployeeLeaveEntitlementDto Map(EmployeeLeaveEntitlement row, Guid reference) => new()
    {
        Id = row.Id, EmployeeReference = reference, LeaveTypeId = row.LeaveTypeId, Year = row.Year,
        EntitledDays = row.EntitledDays, CarriedForwardDays = row.CarriedForwardDays, RowVersion = Version(row.RowVersion)
    };
    private static EmployeeLeaveAdjustmentDto Map(EmployeeLeaveAdjustment row, Guid reference) => new()
    {
        Id = row.Id, EmployeeReference = reference, LeaveTypeId = row.LeaveTypeId, Year = row.Year,
        Days = row.Days, Reason = row.Reason, ApprovedByUserId = row.ApprovedByUserId, ApprovedAt = row.ApprovedAt
    };
    private Task<Employee?> FindEmployee(Guid reference, CancellationToken ct) =>
        Employees.FirstOrDefaultAsync(x => x.PublicId == reference, ct);
    private async Task<decimal> CommittedDays(long employeeId, long typeId, int year, CancellationToken ct)
    {
        var start = new DateOnly(year, 1, 1); var end = start.AddYears(1);
        return await Applications.Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == typeId &&
            x.FromDate >= start && x.FromDate < end &&
            (x.State == LeaveState.Approved || x.State == LeaveState.Submitted))
            .SumAsync(x => (decimal?)x.TotalDays, ct) ?? 0m;
    }
    private async Task<decimal> EffectiveAllowed(long employeeId, LeaveType type, int year, CancellationToken ct)
    {
        var entitlement = await Entitlements.Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == type.Id && x.Year == year)
            .Select(x => (decimal?)(x.EntitledDays + x.CarriedForwardDays)).FirstOrDefaultAsync(ct);
        var changes = await Adjustments.Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == type.Id && x.Year == year)
            .SumAsync(x => (decimal?)x.Days, ct) ?? 0m;
        return (entitlement ?? type.MaxDaysPerYear) + changes;
    }
    public async Task<ApiResponse<IReadOnlyList<LeaveTypeDto>>> GetLeaveTypesAsync(CancellationToken ct = default)
    {
        if (!Authorized) return Error<IReadOnlyList<LeaveTypeDto>>("Institution administrator permission required.", 403);
        var rows = await Types.AsNoTracking().OrderBy(x => x.Code).Take(1001).ToListAsync(ct);
        if (rows.Count > 1000) return Error<IReadOnlyList<LeaveTypeDto>>("Leave type limit exceeded.", 409);
        return ApiResponse<IReadOnlyList<LeaveTypeDto>>.SuccessResponse(rows.Select(Map).ToArray());
    }
    public async Task<ApiResponse<LeaveTypeDto>> SaveLeaveTypeAsync(long? leaveTypeId, SaveLeaveTypeRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<LeaveTypeDto>("Institution administrator permission required.", 403);
        if (request == null || leaveTypeId is <= 0 || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 100 || string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Trim().Length > 30 || request.MaxDaysPerYear is < 0 or > 366)
            return Error<LeaveTypeDto>("Invalid leave type.");
        var code = request.Code.Trim().ToUpperInvariant();
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var row = leaveTypeId.HasValue ? await Types.FirstOrDefaultAsync(x => x.Id == leaveTypeId.Value, token) : null;
                if (leaveTypeId.HasValue && row == null) return Error<LeaveTypeDto>("Leave type not found.", 404);
                if (row != null && !Matches(row.RowVersion, request.RowVersion))
                    return Error<LeaveTypeDto>("Leave type changed; reload.", 409);
                if (await Types.AnyAsync(x => x.Code == code && (!leaveTypeId.HasValue || x.Id != leaveTypeId.Value), token))
                    return Error<LeaveTypeDto>("Leave type code already exists.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new LeaveType { TenantId = user.TenantId, CreatedAt = now, CreatedBy = user.UserId };
                    db.LeaveTypes.Add(row);
                }
                else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
                row.Code = code; row.Name = request.Name.Trim(); row.MaxDaysPerYear = request.MaxDaysPerYear;
                row.IsPaid = request.IsPaid; row.IsActive = request.IsActive;
                await db.SaveChangesAsync(token);
                return ApiResponse<LeaveTypeDto>.SuccessResponse(Map(row), "Leave type saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<LeaveTypeDto>("Leave type changed; reload.", 409); }
        catch (DbUpdateException) { return Error<LeaveTypeDto>("Leave type conflicts with existing data.", 409); }
    }
    public async Task<ApiResponse<IReadOnlyList<EmployeeLeaveEntitlementDto>>> GetEntitlementsAsync(Guid employeeReference, int year, CancellationToken ct = default)
    {
        if (!Authorized) return Error<IReadOnlyList<EmployeeLeaveEntitlementDto>>("Institution administrator permission required.", 403);
        if (employeeReference == Guid.Empty || !ValidYear(year)) return Error<IReadOnlyList<EmployeeLeaveEntitlementDto>>("Invalid employee or year.");
        var employee = await FindEmployee(employeeReference, ct);
        if (employee == null) return Error<IReadOnlyList<EmployeeLeaveEntitlementDto>>("Employee not found.", 404);
        var rows = await Entitlements.AsNoTracking().Where(x => x.EmployeeId == employee.Id && x.Year == year)
            .OrderBy(x => x.LeaveTypeId).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<EmployeeLeaveEntitlementDto>>.SuccessResponse(rows.Select(x => Map(x, employeeReference)).ToArray());
    }
    public async Task<ApiResponse<EmployeeLeaveEntitlementDto>> SaveEntitlementAsync(SaveEmployeeLeaveEntitlementRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<EmployeeLeaveEntitlementDto>("Institution administrator permission required.", 403);
        if (request == null || request.EmployeeReference == Guid.Empty || request.LeaveTypeId <= 0 ||
            !ValidYear(request.Year) || request.EntitledDays is < 0 or > 366 || request.CarriedForwardDays is < 0 or > 366 ||
            !MoneyScale(request.EntitledDays) || !MoneyScale(request.CarriedForwardDays))
            return Error<EmployeeLeaveEntitlementDto>("Invalid entitlement or decimal precision.");
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var employee = await FindEmployee(request.EmployeeReference, token);
                if (employee == null) return Error<EmployeeLeaveEntitlementDto>("Employee not found.", 404);
                var type = await Types.FirstOrDefaultAsync(x => x.Id == request.LeaveTypeId && x.IsActive, token);
                if (type == null) return Error<EmployeeLeaveEntitlementDto>("Active leave type not found.", 404);
                var row = await Entitlements.FirstOrDefaultAsync(x => x.EmployeeId == employee.Id &&
                    x.LeaveTypeId == type.Id && x.Year == request.Year, token);
                if (row != null && !Matches(row.RowVersion, request.RowVersion))
                    return Error<EmployeeLeaveEntitlementDto>("Entitlement changed; reload.", 409);
                if (row == null && !string.IsNullOrWhiteSpace(request.RowVersion))
                    return Error<EmployeeLeaveEntitlementDto>("Entitlement was created concurrently; reload.", 409);
                var changes = await Adjustments.Where(x => x.EmployeeId == employee.Id && x.LeaveTypeId == type.Id && x.Year == request.Year)
                    .SumAsync(x => (decimal?)x.Days, token) ?? 0m;
                var allowed = request.EntitledDays + request.CarriedForwardDays + changes;
                if (allowed < await CommittedDays(employee.Id, type.Id, request.Year, token))
                    return Error<EmployeeLeaveEntitlementDto>("Cannot reduce entitlement below approved and submitted leave.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                if (row == null)
                {
                    row = new EmployeeLeaveEntitlement { TenantId = user.TenantId, EmployeeId = employee.Id,
                        LeaveTypeId = type.Id, Year = request.Year, CreatedAt = now, CreatedBy = user.UserId };
                    db.EmployeeLeaveEntitlements.Add(row);
                }
                else { row.UpdatedAt = now; row.UpdatedBy = user.UserId; }
                row.EntitledDays = request.EntitledDays; row.CarriedForwardDays = request.CarriedForwardDays;
                await db.SaveChangesAsync(token);
                return ApiResponse<EmployeeLeaveEntitlementDto>.SuccessResponse(Map(row, employee.PublicId), "Entitlement saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<EmployeeLeaveEntitlementDto>("Entitlement changed; reload.", 409); }
        catch (DbUpdateException) { return Error<EmployeeLeaveEntitlementDto>("Entitlement conflicts with existing data.", 409); }
    }
    public async Task<ApiResponse<EmployeeLeaveAdjustmentDto>> AddAdjustmentAsync(CreateEmployeeLeaveAdjustmentRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<EmployeeLeaveAdjustmentDto>("Institution administrator permission required.", 403);
        if (request == null || request.EmployeeReference == Guid.Empty || request.LeaveTypeId <= 0 ||
            !ValidYear(request.Year) || request.Days == 0 || request.Days is < -366 or > 366 ||
            !MoneyScale(request.Days) || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > 1000)
            return Error<EmployeeLeaveAdjustmentDto>("Invalid leave adjustment or decimal precision.");
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var employee = await FindEmployee(request.EmployeeReference, token);
                if (employee == null) return Error<EmployeeLeaveAdjustmentDto>("Employee not found.", 404);
                var type = await Types.FirstOrDefaultAsync(x => x.Id == request.LeaveTypeId && x.IsActive, token);
                if (type == null) return Error<EmployeeLeaveAdjustmentDto>("Active leave type not found.", 404);
                var reason = request.Reason.Trim();
                // Existing DTO has no client-request ID; prevent exact duplicate adjustment replay.
                var replay = await Adjustments.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employee.Id &&
                    x.LeaveTypeId == type.Id && x.Year == request.Year && x.Days == request.Days && x.Reason == reason, token);
                if (replay != null) return ApiResponse<EmployeeLeaveAdjustmentDto>.SuccessResponse(Map(replay, employee.PublicId), "Existing identical adjustment returned.");
                var allowed = await EffectiveAllowed(employee.Id, type, request.Year, token) + request.Days;
                if (allowed < await CommittedDays(employee.Id, type.Id, request.Year, token))
                    return Error<EmployeeLeaveAdjustmentDto>("Adjustment would overdraw approved or submitted leave.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                var row = new EmployeeLeaveAdjustment
                {
                    TenantId = user.TenantId, EmployeeId = employee.Id, LeaveTypeId = type.Id, Year = request.Year,
                    Days = request.Days, Reason = reason, ApprovedByUserId = user.UserId,
                    ApprovedAt = now, CreatedAt = now, CreatedBy = user.UserId
                };
                db.EmployeeLeaveAdjustments.Add(row);
                await db.SaveChangesAsync(token);
                return ApiResponse<EmployeeLeaveAdjustmentDto>.SuccessResponse(Map(row, employee.PublicId), "Audited leave adjustment recorded.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<EmployeeLeaveAdjustmentDto>("Adjustment changed; reload.", 409); }
        catch (DbUpdateException) { return Error<EmployeeLeaveAdjustmentDto>("Adjustment conflicts with existing data.", 409); }
    }
    public async Task<ApiResponse<IReadOnlyList<LeaveBalanceDto>>> GetBalancesAsync(Guid employeeReference, int year, CancellationToken ct = default)
    {
        if (!Authorized) return Error<IReadOnlyList<LeaveBalanceDto>>("Institution administrator permission required.", 403);
        if (employeeReference == Guid.Empty || !ValidYear(year)) return Error<IReadOnlyList<LeaveBalanceDto>>("Invalid employee or year.");
        var employee = await FindEmployee(employeeReference, ct);
        if (employee == null) return Error<IReadOnlyList<LeaveBalanceDto>>("Employee not found.", 404);
        var types = await Types.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var entitlements = await Entitlements.AsNoTracking().Where(x => x.EmployeeId == employee.Id && x.Year == year)
            .ToDictionaryAsync(x => x.LeaveTypeId, ct);
        var adjustments = await Adjustments.AsNoTracking().Where(x => x.EmployeeId == employee.Id && x.Year == year)
            .GroupBy(x => x.LeaveTypeId).Select(g => new { Id = g.Key, Days = g.Sum(x => x.Days) }).ToListAsync(ct);
        var changes = adjustments.ToDictionary(x => x.Id, x => x.Days);
        var start = new DateOnly(year, 1, 1); var end = start.AddYears(1);
        var usage = await Applications.AsNoTracking().Where(x => x.EmployeeId == employee.Id &&
            x.FromDate >= start && x.FromDate < end)
            .GroupBy(x => x.LeaveTypeId).Select(g => new
            {
                Id = g.Key,
                Used = g.Where(x => x.State == LeaveState.Approved).Sum(x => (decimal?)x.TotalDays) ?? 0m,
                Pending = g.Where(x => x.State == LeaveState.Submitted).Sum(x => (decimal?)x.TotalDays) ?? 0m
            }).ToListAsync(ct);
        var consumed = usage.ToDictionary(x => x.Id);
        var result = types.Select(type =>
        {
            var allowed = (entitlements.TryGetValue(type.Id, out var entry)
                ? entry.EntitledDays + entry.CarriedForwardDays : type.MaxDaysPerYear) +
                (changes.TryGetValue(type.Id, out var change) ? change : 0m);
            consumed.TryGetValue(type.Id, out var used);
            return new LeaveBalanceDto
            {
                LeaveTypeId = type.Id, LeaveTypeName = type.Name, AllowedDays = allowed,
                UsedDays = used?.Used ?? 0m,
                // Remaining reserves submitted leave, matching the employee self-service portal.
                RemainingDays = allowed - (used?.Used ?? 0m) - (used?.Pending ?? 0m)
            };
        }).ToArray();
        return ApiResponse<IReadOnlyList<LeaveBalanceDto>>.SuccessResponse(result);
    }
}
