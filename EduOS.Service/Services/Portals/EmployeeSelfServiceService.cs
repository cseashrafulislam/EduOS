using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Portals;

public sealed class EmployeeSelfServiceService : IEmployeeSelfServiceService
{
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<EmployeeAttendance> _attendance;
    private readonly IGenericRepository<LeaveApplication> _leaveApplications;
    private readonly IGenericRepository<LeaveType> _leaveTypes;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<EmployeeSelfServiceService> _logger;

    public EmployeeSelfServiceService(IGenericRepository<Employee> employees, IGenericRepository<EmployeeAttendance> attendance, IGenericRepository<LeaveApplication> leaveApplications, IGenericRepository<LeaveType> leaveTypes, ICurrentUserService currentUser, ILogger<EmployeeSelfServiceService> logger)
    {
        _employees = employees;
        _attendance = attendance;
        _leaveApplications = leaveApplications;
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<EmployeePortalProfileDto>> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<EmployeePortalProfileDto>.ErrorResponse("Employee self-service is not available for this account.", 403);
        try
        {
            var profile = await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.IsActive).Select(x => new EmployeePortalProfileDto { Reference = x.PublicId, EmployeeCode = x.EmployeeCode, FullName = x.FullName, Phone = x.Phone, Email = x.Email, DesignationId = x.DesignationId, DepartmentId = x.DepartmentId, JoiningDate = x.JoiningDate, Salary = x.Salary, PhotoUrl = x.PhotoUrl, Qualification = x.Qualification, Experience = x.Experience, IsTeacher = x.IsTeacher }).SingleOrDefaultAsync(cancellationToken);
            return profile == null ? ApiResponse<EmployeePortalProfileDto>.ErrorResponse("No active employee profile is linked to this account.", 404) : ApiResponse<EmployeePortalProfileDto>.SuccessResponse(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee self-service profile failed for user {UserId}", _currentUser.UserId);
            return ApiResponse<EmployeePortalProfileDto>.ErrorResponse("Employee profile could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>> GetAttendanceAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("Employee self-service is not available for this account.", 403);
        var from = (fromDate ?? DateTime.UtcNow.AddDays(-30)).Date;
        var to = (toDate ?? DateTime.UtcNow).Date;
        if (from > to || (to - from).TotalDays > 366) return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("Attendance date range is invalid.", 400);
        try
        {
            var employeeId = await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.IsActive).Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
            if (!employeeId.HasValue) return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("No active employee profile is linked to this account.", 404);
            IReadOnlyList<EmployeePortalAttendanceDto> rows = await _attendance.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.EmployeeId == employeeId.Value && x.Date >= from && x.Date <= to).OrderByDescending(x => x.Date).Select(x => new EmployeePortalAttendanceDto { Date = x.Date, Status = x.Status, InTime = x.InTime, OutTime = x.OutTime, OvertimeHours = x.OvertimeHours, Remarks = x.Remarks }).ToListAsync(cancellationToken);
            return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.SuccessResponse(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee self-service attendance failed for user {UserId}", _currentUser.UserId);
            return ApiResponse<IReadOnlyList<EmployeePortalAttendanceDto>>.ErrorResponse("Employee attendance could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>> GetLeaveHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.ErrorResponse("Employee self-service is not available for this account.", 403);
        try
        {
            var hasActiveEmployee = await _employees.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.IsActive, cancellationToken);
            if (!hasActiveEmployee) return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.ErrorResponse("No active employee profile is linked to this account.", 404);
            var leaveTypes = _leaveTypes.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId);
            IReadOnlyList<EmployeePortalLeaveDto> rows = await (from leave in _leaveApplications.GetQueryable().AsNoTracking() join leaveType in leaveTypes on leave.LeaveTypeId equals leaveType.Id where leave.TenantId == _currentUser.TenantId && leave.UserId == _currentUser.UserId && leave.UserType == "Employee" orderby leave.FromDate descending, leave.Id descending select new EmployeePortalLeaveDto { Id = leave.Id, LeaveType = leaveType.Name, FromDate = leave.FromDate, ToDate = leave.ToDate, TotalDays = leave.TotalDays, Reason = leave.Reason, Status = leave.Status, Remarks = leave.Remarks }).ToListAsync(cancellationToken);
            return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.SuccessResponse(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee self-service leave history failed for user {UserId}", _currentUser.UserId);
            return ApiResponse<IReadOnlyList<EmployeePortalLeaveDto>>.ErrorResponse("Employee leave history could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<EmployeePortalLeaveDto>> ApplyLeaveAsync(EmployeePortalLeaveApplyDto request, CancellationToken cancellationToken = default)
    {
        if (!CanUsePortal()) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Employee self-service is not available for this account.", 403);
        var from = request.FromDate.Date;
        var to = request.ToDate.Date;
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (request.LeaveTypeId <= 0 || from > to || reason.Length < 3 || reason.Length > 1000) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave application data is invalid.", 400);
        var totalDays = (to - from).Days + 1;
        if (totalDays > 366 || from.Year != to.Year) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave period must stay within one calendar year and cannot exceed 366 days.", 400);
        try
        {
            var hasActiveEmployee = await _employees.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.IsActive, cancellationToken);
            if (!hasActiveEmployee) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("No active employee profile is linked to this account.", 404);
            var leaveType = await _leaveTypes.GetQueryable().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == request.LeaveTypeId && x.IsActive && !x.IsDeleted, cancellationToken);
            if (leaveType == null) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave type was not found or is inactive.", 404);
            if (leaveType.MaxDaysPerYear > 0)
            {
                var yearStart = new DateTime(from.Year, 1, 1);
                var yearEnd = yearStart.AddYears(1).AddDays(-1);
                var consumedDays = await _leaveApplications.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.UserType == "Employee" && x.LeaveTypeId == leaveType.Id && !x.IsDeleted && (x.Status == "Pending" || x.Status == "Approved") && x.FromDate >= yearStart && x.ToDate <= yearEnd).SumAsync(x => (int?)x.TotalDays, cancellationToken) ?? 0;
                if (consumedDays + totalDays > leaveType.MaxDaysPerYear) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Requested leave exceeds the remaining annual entitlement for this leave type.", 400);
            }
            var overlaps = await _leaveApplications.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.UserId == _currentUser.UserId && x.UserType == "Employee" && !x.IsDeleted && (x.Status == "Pending" || x.Status == "Approved") && x.FromDate <= to && x.ToDate >= from, cancellationToken);
            if (overlaps) return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("An existing pending or approved leave overlaps this period.", 409);
            var entity = new LeaveApplication { TenantId = _currentUser.TenantId, UserId = _currentUser.UserId, UserType = "Employee", LeaveTypeId = leaveType.Id, FromDate = from, ToDate = to, TotalDays = totalDays, Reason = reason, Status = "Pending", CreatedBy = _currentUser.UserId };
            await _leaveApplications.AddAsync(entity);
            await _leaveApplications.UnitOfWork.SaveChangesAsync(cancellationToken);
            var dto = new EmployeePortalLeaveDto { Id = entity.Id, LeaveType = leaveType.Name, FromDate = entity.FromDate, ToDate = entity.ToDate, TotalDays = entity.TotalDays, Reason = entity.Reason, Status = entity.Status, Remarks = entity.Remarks };
            return ApiResponse<EmployeePortalLeaveDto>.SuccessResponse(dto, "Leave application submitted successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee self-service leave application failed for user {UserId}", _currentUser.UserId);
            return ApiResponse<EmployeePortalLeaveDto>.ErrorResponse("Leave application could not be submitted.", 500);
        }
    }

    private bool CanUsePortal() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsInRole("Teacher") || _currentUser.IsInRole("Staff"));
}
