using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Portals;

public sealed class EmployeeSelfServiceService : IEmployeeSelfServiceService
{
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<EmployeeAttendance> _attendance;
    private readonly IGenericRepository<EmployeeLeaveApplication> _leaveApplications;
    private readonly IGenericRepository<LeaveType> _leaveTypes;
    private readonly IGenericRepository<EmployeeLeaveEntitlement> _entitlements;
    private readonly IGenericRepository<EmployeeLeaveAdjustment> _adjustments;
    private readonly IGenericRepository<SalaryStructure> _structures;
    private readonly IGenericRepository<SalaryStructureLine> _structureLines;
    private readonly IGenericRepository<SalaryComponent> _salaryComponents;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<EmployeeSelfServiceService> _logger;

    public EmployeeSelfServiceService(IGenericRepository<Employee> employees, IGenericRepository<EmployeeAttendance> attendance,
        IGenericRepository<EmployeeLeaveApplication> leaveApplications, IGenericRepository<LeaveType> leaveTypes,
        IGenericRepository<EmployeeLeaveEntitlement> entitlements, IGenericRepository<EmployeeLeaveAdjustment> adjustments,
        IGenericRepository<SalaryStructure> structures, IGenericRepository<SalaryStructureLine> structureLines,
        IGenericRepository<SalaryComponent> salaryComponents, ICurrentUserService currentUser,
        ILogger<EmployeeSelfServiceService> logger)
    {
        _employees = employees;
        _attendance = attendance;
        _leaveApplications = leaveApplications;
        _leaveTypes = leaveTypes;
        _entitlements = entitlements;
        _adjustments = adjustments;
        _structures = structures;
        _structureLines = structureLines;
        _salaryComponents = salaryComponents;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<EmployeePortalProfileDto>> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<EmployeePortalProfileDto>.ErrorResponse("Employee self-service is not available.", 403);
        try
        {
            var employee = await CurrentEmployeeQuery().FirstOrDefaultAsync(cancellationToken);
            if (employee == null) return ApiResponse<EmployeePortalProfileDto>.ErrorResponse("Active employee profile not found.", 404);
            var dto = new EmployeePortalProfileDto
            {
                Reference = employee.PublicId, EmployeeCode = employee.EmployeeCode, FullName = employee.FullName,
                Phone = employee.Phone ?? string.Empty, Email = employee.Email,
                DesignationId = employee.DesignationId, OrganizationUnitId = employee.OrganizationUnitId,
                JoiningDate = employee.JoiningDate,
                PhotoUrl = employee.PhotoUrl, CanTeach = employee.CanTeach, State = employee.State
            };
            return ApiResponse<EmployeePortalProfileDto>.SuccessResponse(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee profile failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<EmployeePortalProfileDto>.ErrorResponse("Employee profile could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>> GetAttendanceAsync(
        DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("Employee self-service is not available.", 403);
        var from = DateOnly.FromDateTime((fromDate ?? DateTime.UtcNow.AddDays(-30)).Date);
        var to = DateOnly.FromDateTime((toDate ?? DateTime.UtcNow).Date);
        if (from > to || to.DayNumber - from.DayNumber > 366)
            return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("Attendance date range is invalid.");
        try
        {
            var employee = await CurrentEmployeeQuery().Select(x => new { x.Id }).FirstOrDefaultAsync(cancellationToken);
            if (employee == null) return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("Active employee profile not found.", 404);
            var records = await _attendance.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId &&
                x.EmployeeId == employee.Id && x.AttendanceDate >= from && x.AttendanceDate <= to)
                .OrderByDescending(x => x.AttendanceDate)
                .Select(x => new { x.AttendanceDate, x.State, x.InTime, x.OutTime, x.OvertimeHours, x.Remarks })
                .ToListAsync(cancellationToken);
            IReadOnlyList<EmployeePortalAttendanceDto> result = records.Select(x => new EmployeePortalAttendanceDto
            {
                AttendanceDate = x.AttendanceDate, State = x.State,
                InTime = x.InTime, OutTime = x.OutTime,
                OvertimeHours = x.OvertimeHours, Remarks = x.Remarks
            }).ToList();
            return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.SuccessResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee attendance failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("Attendance could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>> GetLeaveHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.ErrorResponse("Employee self-service is not available.", 403);
        try
        {
            var employee = await CurrentEmployeeQuery().Select(x => new { x.Id }).FirstOrDefaultAsync(cancellationToken);
            if (employee == null) return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.ErrorResponse("Active employee profile not found.", 404);
            var tenantId = _currentUser.TenantId;
            var records = await (from leave in _leaveApplications.GetQueryable().AsNoTracking()
                join type in _leaveTypes.GetQueryable().AsNoTracking() on leave.LeaveTypeId equals type.Id
                where leave.TenantId == tenantId && type.TenantId == tenantId && leave.EmployeeId == employee.Id
                orderby leave.FromDate descending, leave.Id descending
                select new { leave.Id, TypeName = type.Name, leave.FromDate, leave.ToDate, leave.TotalDays,
                    leave.Reason, leave.State, leave.ReviewNote }).ToListAsync(cancellationToken);
            IReadOnlyList<EmployeePortalLeaveDto> result = records.Select(x => new EmployeePortalLeaveDto
            {
                Id = x.Id, LeaveTypeName = x.TypeName,
                FromDate = x.FromDate, ToDate = x.ToDate,
                TotalDays = x.TotalDays, Reason = x.Reason,
                State = x.State, ReviewNote = x.ReviewNote
            }).ToList();
            return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.SuccessResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leave history failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.ErrorResponse("Leave history could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<EmployeePortalLeaveBalanceDto>>> GetLeaveBalancesAsync(
        int? year = null, CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<IReadOnlyList<EmployeePortalLeaveBalanceDto>>.ErrorResponse("Employee self-service is not available.", 403);
        var targetYear = year ?? DateTime.UtcNow.Year;
        if (targetYear < 2000 || targetYear > DateTime.UtcNow.Year + 1)
            return ApiResponse<IReadOnlyList<EmployeePortalLeaveBalanceDto>>.ErrorResponse("Leave balance year is invalid.");
        try
        {
            var employee = await CurrentEmployeeQuery().Select(x => new { x.Id }).FirstOrDefaultAsync(cancellationToken);
            if (employee == null) return ApiResponse<IReadOnlyList<EmployeePortalLeaveBalanceDto>>.ErrorResponse("Active employee profile not found.", 404);
            var tenantId = _currentUser.TenantId;
            var from = new DateOnly(targetYear, 1, 1);
            var until = from.AddYears(1);
            var types = await _leaveTypes.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
                .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.MaxDaysPerYear }).ToListAsync(cancellationToken);
            var entitlements = await _entitlements.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id && x.Year == targetYear)
                .GroupBy(x => x.LeaveTypeId).Select(g => new
                {
                    TypeId = g.Key, Days = g.Sum(x => x.EntitledDays + x.CarriedForwardDays)
                }).ToListAsync(cancellationToken);
            var adjustments = await _adjustments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id && x.Year == targetYear)
                .GroupBy(x => x.LeaveTypeId).Select(g => new { TypeId = g.Key, Days = g.Sum(x => x.Days) })
                .ToListAsync(cancellationToken);
            var usage = await _leaveApplications.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id && x.FromDate >= from && x.FromDate < until)
                .GroupBy(x => x.LeaveTypeId).Select(g => new
                {
                    TypeId = g.Key,
                    UsedDays = g.Where(x => x.State == LeaveState.Approved).Sum(x => (decimal?)x.TotalDays) ?? 0m,
                    PendingDays = g.Where(x => x.State == LeaveState.Submitted).Sum(x => (decimal?)x.TotalDays) ?? 0m
                }).ToListAsync(cancellationToken);
            var eMap = entitlements.ToDictionary(x => x.TypeId, x => x.Days);
            var aMap = adjustments.ToDictionary(x => x.TypeId, x => x.Days);
            var uMap = usage.ToDictionary(x => x.TypeId);
            IReadOnlyList<EmployeePortalLeaveBalanceDto> rows = types.Select(type =>
            {
                var maximum = eMap.TryGetValue(type.Id, out var days) ? days : type.MaxDaysPerYear;
                maximum += aMap.TryGetValue(type.Id, out var adjustment) ? adjustment : 0m;
                uMap.TryGetValue(type.Id, out var consumed);
                var used = consumed?.UsedDays ?? 0m;
                var pending = consumed?.PendingDays ?? 0m;
                return new EmployeePortalLeaveBalanceDto
                {
                    LeaveTypeId = type.Id, LeaveTypeName = type.Name,
                    AnnualEntitlement = maximum,
                    UsedDays = used, PendingDays = pending,
                    RemainingDays = Math.Max(0m, maximum - used - pending)
                };
            }).ToList();
            return ApiResponse<IReadOnlyList<EmployeePortalLeaveBalanceDto>>.SuccessResponse(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leave balance failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<IReadOnlyList<EmployeePortalLeaveBalanceDto>>.ErrorResponse("Leave balances could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<EmployeePortalLeaveDto>> ApplyLeaveAsync(EmployeePortalLeaveApplyDto request, CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Employee self-service is not available.", 403);
        if (request == null) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave request is required.");
        var from = request.FromDate;
        var to = request.ToDate;
        var reason = request.Reason?.Trim() ?? string.Empty;
        var requestedDays = to.DayNumber - from.DayNumber + 1;
        if (request.ClientRequestId == Guid.Empty || request.LeaveTypeId <= 0 || from == default ||
            from > to || from.Year != to.Year || requestedDays > 366 ||
            reason.Length is < 3 or > 2000)
            return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave application is invalid.");
        try
        {
            var employee = await CurrentEmployeeQuery().Select(x => new { x.Id }).FirstOrDefaultAsync(cancellationToken);
            if (employee == null) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Active employee profile not found.", 404);
            var tenantId = _currentUser.TenantId;
            var type = await _leaveTypes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                x.Id == request.LeaveTypeId && x.IsActive, cancellationToken);
            if (type == null) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave type unavailable.", 404);

            using var scope = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
            var replay = await _leaveApplications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.EmployeeId == employee.Id &&
                x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, cancellationToken);
            if (replay != null)
            {
                if (replay.LeaveTypeId != type.Id || replay.FromDate != from ||
                    replay.ToDate != to || replay.Reason != reason)
                    return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Request ID used for different leave details.", 409);
                scope.Complete();
                return ApiResponse<EmployeePortalLeaveDto>.SuccessResponse(ToLeaveDto(replay, type.Name), "Leave request already submitted.");
            }
            var existing = await _leaveApplications.GetQueryable().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id && x.FromDate <= to && x.ToDate >= from &&
                (x.State == LeaveState.Submitted || x.State == LeaveState.Approved)).ToListAsync(cancellationToken);
            if (existing.Any())
            {
                var exact = existing.FirstOrDefault(x => x.LeaveTypeId == type.Id && x.FromDate == from &&
                    x.ToDate == to && x.Reason == reason);
                if (exact != null)
                    return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave already exists; use the original request ID for retry.", 409);
                return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("An approved or submitted leave overlaps this period.", 409);
            }

            var entitlement = await _entitlements.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id && x.LeaveTypeId == type.Id && x.Year == from.Year)
                .Select(x => (decimal?)(x.EntitledDays + x.CarriedForwardDays)).SumAsync(cancellationToken);
            var adjustment = await _adjustments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id && x.LeaveTypeId == type.Id && x.Year == from.Year)
                .SumAsync(x => (decimal?)x.Days, cancellationToken) ?? 0m;
            var available = (entitlement ?? type.MaxDaysPerYear) + adjustment;
            var yearStart = new DateOnly(from.Year, 1, 1);
            var yearEnd = yearStart.AddYears(1);
            var used = await _leaveApplications.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id && x.LeaveTypeId == type.Id &&
                x.FromDate >= yearStart && x.FromDate < yearEnd &&
                (x.State == LeaveState.Submitted || x.State == LeaveState.Approved))
                .SumAsync(x => (decimal?)x.TotalDays, cancellationToken) ?? 0m;
            if (type.MaxDaysPerYear > 0 && used + requestedDays > available)
                return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave exceeds remaining entitlement.");
            var entity = new EmployeeLeaveApplication
            {
                TenantId = tenantId, ClientRequestId = request.ClientRequestId, EmployeeId = employee.Id,
                LeaveTypeId = type.Id, FromDate = from, ToDate = to, TotalDays = requestedDays,
                Reason = reason, State = LeaveState.Submitted,
                CreatedBy = _currentUser.UserId, CreatedAt = DateTime.UtcNow
            };
            await _leaveApplications.AddAsync(entity);
            await _leaveApplications.UnitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
            return ApiResponse<EmployeePortalLeaveDto>.SuccessResponse(ToLeaveDto(entity, type.Name), "Leave application submitted.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Leave conflict for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave request conflicts with another submission.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized leave request rejected for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave request conflicts with another submission.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leave application failed for tenant {TenantId}", _currentUser.TenantId);
            return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave could not be submitted.", 500);
        }
    }

    private IQueryable<Employee> CurrentEmployeeQuery() => _employees.GetQueryable().AsNoTracking().Where(x =>
        x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.State == EmployeeState.Active);
    private static EmployeePortalLeaveDto ToLeaveDto(EmployeeLeaveApplication leave, string leaveType) => new()
    {
        Id = leave.Id, LeaveTypeName = leaveType,
        FromDate = leave.FromDate, ToDate = leave.ToDate,
        TotalDays = leave.TotalDays, Reason = leave.Reason,
        State = leave.State, ReviewNote = leave.ReviewNote
    };
    private bool CanUsePortal() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 &&
        (_currentUser.IsInRole("Teacher") || _currentUser.IsInRole("Staff"));
}
