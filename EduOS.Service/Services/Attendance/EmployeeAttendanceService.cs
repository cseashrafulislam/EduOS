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

namespace EduOS.Service.Services.Attendance;

/// <summary>Institution-wide manual employee attendance. Campus-specific staff access is deliberately not granted.</summary>
public sealed class EmployeeAttendanceService : IEmployeeAttendanceService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public EmployeeAttendanceService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool Authorized => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int code = 400) => ApiResponse<T>.ErrorResponse(message, code);
    private IQueryable<EmployeeAttendance> Attendance => db.EmployeeAttendances.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<Employee> Employees => db.Employees.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static bool Match(byte[] actual, string supplied)
    {
        try { return actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static bool ValidState(AttendanceState state) => Enum.IsDefined(state);
    private static bool ValidEntry(SaveEmployeeAttendanceRequestDto x)
    {
        if (x.ClientRequestId == Guid.Empty || x.EmployeeReference == Guid.Empty || x.AttendanceDate == default ||
            !ValidState(x.State) || x.Remarks?.Length > 500 || x.OvertimeHours is < 0 or > 24 ||
            x.OvertimeHours.HasValue && decimal.Round(x.OvertimeHours.Value, 2) != x.OvertimeHours.Value)
            return false;
        if (x.State is AttendanceState.Absent or AttendanceState.Leave or AttendanceState.Excused)
            return !x.InTime.HasValue && !x.OutTime.HasValue && (!x.OvertimeHours.HasValue || x.OvertimeHours == 0);
        if (x.OutTime.HasValue && !x.InTime.HasValue) return false;
        return true;
    }
    private static EmployeeAttendanceDto Map(EmployeeAttendance row, Employee employee) => new()
    {
        Id = row.Id, EmployeeReference = employee.PublicId, EmployeeCode = employee.EmployeeCode,
        EmployeeName = employee.FullName, AttendanceDate = row.AttendanceDate, State = row.State,
        InTime = row.InTime, OutTime = row.OutTime, OvertimeHours = row.OvertimeHours,
        Remarks = row.Remarks, RowVersion = Version(row.RowVersion)
    };
    private async Task<Employee?> FindEmployee(Guid reference, CancellationToken ct) =>
        await Employees.FirstOrDefaultAsync(x => x.PublicId == reference, ct);
    private async Task<bool> ApprovedLeave(long employeeId, DateOnly date, CancellationToken ct) =>
        await db.EmployeeLeaveApplications.AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.EmployeeId == employeeId &&
            x.State == LeaveState.Approved && x.FromDate <= date && x.ToDate >= date, ct);

    public async Task<ApiResponse<PagedResult<EmployeeAttendanceDto>>> GetAttendanceAsync(
        Guid? employeeReference, DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Authorized) return Error<PagedResult<EmployeeAttendanceDto>>("Institution administrator permission required.", 403);
        if (fromDate == default || toDate == default || fromDate > toDate ||
            toDate.DayNumber - fromDate.DayNumber > 366 || page < 1 || pageSize is < 1 or > 100)
            return Error<PagedResult<EmployeeAttendanceDto>>("Invalid date range or pagination.");
        if (employeeReference == Guid.Empty) return Error<PagedResult<EmployeeAttendanceDto>>("Invalid employee reference.");
        var query = from row in Attendance.AsNoTracking()
                    join employee in Employees.AsNoTracking() on row.EmployeeId equals employee.Id
                    where row.AttendanceDate >= fromDate && row.AttendanceDate <= toDate &&
                          (!employeeReference.HasValue || employee.PublicId == employeeReference.Value)
                    select new { row, employee };
        var total = await query.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue) return Error<PagedResult<EmployeeAttendanceDto>>("Page out of range.");
        var records = await query.OrderByDescending(x => x.row.AttendanceDate).ThenBy(x => x.employee.EmployeeCode)
            .ThenBy(x => x.row.Id).Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<EmployeeAttendanceDto>>.SuccessResponse(new PagedResult<EmployeeAttendanceDto>
        {
            Items = records.Select(x => Map(x.row, x.employee)).ToList(), Page = page, PageSize = pageSize, TotalCount = total
        });
    }

    public async Task<ApiResponse<EmployeeAttendanceDto>> RecordAttendanceAsync(
        SaveEmployeeAttendanceRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<EmployeeAttendanceDto>("Institution administrator permission required.", 403);
        if (request == null || !ValidEntry(request) || !string.IsNullOrEmpty(request.RowVersion))
            return Error<EmployeeAttendanceDto>("Invalid attendance request; new entries must not include a row version.");
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var employee = await FindEmployee(request.EmployeeReference, token);
                if (employee == null) return Error<EmployeeAttendanceDto>("Employee not found.", 404);
                if (employee.State != EmployeeState.Active || request.AttendanceDate < employee.JoiningDate ||
                    employee.LeavingDate.HasValue && request.AttendanceDate > employee.LeavingDate.Value)
                    return Error<EmployeeAttendanceDto>("Employee is not active for the attendance date.", 409);
                var remarks = Trim(request.Remarks);
                var existing = await Attendance.FirstOrDefaultAsync(x =>
                    x.EmployeeId == employee.Id && x.AttendanceDate == request.AttendanceDate, token);
                if (existing != null)
                {
                    // The tenant/employee/date unique index makes identical retries safe without rewriting the row.
                    if (existing.State == request.State && existing.InTime == request.InTime &&
                        existing.OutTime == request.OutTime && existing.OvertimeHours == request.OvertimeHours &&
                        existing.Remarks == remarks)
                        return ApiResponse<EmployeeAttendanceDto>.SuccessResponse(Map(existing, employee), "Attendance already recorded.");
                    return Error<EmployeeAttendanceDto>("Attendance exists. Use the audited correction workflow.", 409);
                }
                var onLeave = await ApprovedLeave(employee.Id, request.AttendanceDate, token);
                if (onLeave != (request.State == AttendanceState.Leave))
                    return Error<EmployeeAttendanceDto>(onLeave ? "Approved leave must be recorded as Leave." :
                        "Leave requires an approved leave application.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                var row = new EmployeeAttendance
                {
                    TenantId = user.TenantId, EmployeeId = employee.Id, AttendanceDate = request.AttendanceDate,
                    State = request.State, InTime = request.InTime, OutTime = request.OutTime,
                    OvertimeHours = request.OvertimeHours, Remarks = remarks, SourceCode = "MANUAL",
                    RecordedByUserId = user.UserId, RecordedAt = now, CreatedAt = now, CreatedBy = user.UserId
                };
                db.EmployeeAttendances.Add(row);
                await db.SaveChangesAsync(token);
                return ApiResponse<EmployeeAttendanceDto>.SuccessResponse(Map(row, employee), "Attendance recorded.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<EmployeeAttendanceDto>("Attendance changed. Reload.", 409); }
        catch (DbUpdateException) { return Error<EmployeeAttendanceDto>("Attendance was recorded concurrently. Reload.", 409); }
    }

    public async Task<ApiResponse<EmployeeAttendanceDto>> CorrectAttendanceAsync(
        long attendanceId, AttendanceCorrectionRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<EmployeeAttendanceDto>("Institution administrator permission required.", 403);
        if (attendanceId <= 0 || request == null || !ValidState(request.NewState) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000 ||
            string.IsNullOrWhiteSpace(request.RowVersion))
            return Error<EmployeeAttendanceDto>("Valid state, reason and row version are required.");
        try
        {
            return await ((IUnitOfWork)db).ExecuteInTransactionAsync(async token =>
            {
                var row = await Attendance.FirstOrDefaultAsync(x => x.Id == attendanceId, token);
                if (row == null) return Error<EmployeeAttendanceDto>("Attendance not found.", 404);
                if (!Match(row.RowVersion, request.RowVersion))
                    return Error<EmployeeAttendanceDto>("Attendance changed. Reload.", 409);
                var employee = await Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == row.EmployeeId, token);
                if (employee == null) return Error<EmployeeAttendanceDto>("Employee not found.", 409);
                if (row.State == request.NewState)
                    return Error<EmployeeAttendanceDto>("Attendance already has this state.", 409);
                var onLeave = await ApprovedLeave(employee.Id, row.AttendanceDate, token);
                if (onLeave != (request.NewState == AttendanceState.Leave))
                    return Error<EmployeeAttendanceDto>(onLeave ? "Approved leave must remain Leave." :
                        "Leave requires an approved leave application.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                db.EmployeeAttendanceAdjustments.Add(new EmployeeAttendanceAdjustment
                {
                    TenantId = user.TenantId, EmployeeAttendanceId = row.Id, PreviousState = row.State,
                    NewState = request.NewState, Reason = request.Reason.Trim(),
                    ChangedByUserId = user.UserId, ChangedAt = now, CreatedAt = now, CreatedBy = user.UserId
                });
                row.State = request.NewState;
                if (request.NewState is AttendanceState.Absent or AttendanceState.Leave or AttendanceState.Excused)
                { row.InTime = null; row.OutTime = null; row.OvertimeHours = null; }
                row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                await db.SaveChangesAsync(token);
                return ApiResponse<EmployeeAttendanceDto>.SuccessResponse(Map(row, employee), "Attendance corrected with audit history.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<EmployeeAttendanceDto>("Attendance changed. Reload.", 409); }
        catch (DbUpdateException) { return Error<EmployeeAttendanceDto>("Attendance correction conflicts with existing data.", 409); }
    }

    public async Task<ApiResponse<IReadOnlyList<EmployeeAttendanceAdjustmentDto>>> GetCorrectionsAsync(
        long attendanceId, CancellationToken ct = default)
    {
        if (!Authorized) return Error<IReadOnlyList<EmployeeAttendanceAdjustmentDto>>("Institution administrator permission required.", 403);
        if (attendanceId <= 0 || !await Attendance.AsNoTracking().AnyAsync(x => x.Id == attendanceId, ct))
            return Error<IReadOnlyList<EmployeeAttendanceAdjustmentDto>>("Attendance not found.", 404);
        var history = await db.EmployeeAttendanceAdjustments.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && x.EmployeeAttendanceId == attendanceId && !x.IsDeleted)
            .OrderByDescending(x => x.ChangedAt).ThenByDescending(x => x.Id).Take(501).ToListAsync(ct);
        if (history.Count > 500) return Error<IReadOnlyList<EmployeeAttendanceAdjustmentDto>>("Too many corrections; request archival export.", 409);
        return ApiResponse<IReadOnlyList<EmployeeAttendanceAdjustmentDto>>.SuccessResponse(history.Select(x =>
            new EmployeeAttendanceAdjustmentDto
            {
                Id = x.Id, EmployeeAttendanceId = x.EmployeeAttendanceId, PreviousState = x.PreviousState,
                NewState = x.NewState, Reason = x.Reason, ChangedByUserId = x.ChangedByUserId,
                ChangedAt = x.ChangedAt
            }).ToArray());
    }
}
