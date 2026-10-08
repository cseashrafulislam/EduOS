using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.HR;

public sealed class HrAdminService : IHrAdminService
{
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<Designation> _designations;
    private readonly IGenericRepository<OrganizationUnit> _organizations;
    private readonly IGenericRepository<EmployeeLeaveApplication> _leaves;
    private readonly IGenericRepository<LeaveType> _leaveTypes;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;

    public HrAdminService(IGenericRepository<Employee> employees, IGenericRepository<Designation> designations,
        IGenericRepository<OrganizationUnit> organizations, IGenericRepository<EmployeeLeaveApplication> leaves,
        IGenericRepository<LeaveType> leaveTypes, IUnitOfWork uow, ICurrentUserService user, TimeProvider clock)
    {
        _employees = employees;
        _designations = designations;
        _organizations = organizations;
        _leaves = leaves;
        _leaveTypes = leaveTypes;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<ApiResponse<PagedResult<HrEmployeeRowDto>>> GetEmployeesAsync(HrEmployeeQueryDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return ApiResponse<PagedResult<HrEmployeeRowDto>>.ErrorResponse("HR access is required.", 403);
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var tenantId = _user.TenantId;
        var employees = _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId);
        if (request.DepartmentId.HasValue) employees = employees.Where(x => x.OrganizationUnitId == request.DepartmentId);
        if (request.IsTeacher.HasValue) employees = employees.Where(x => x.CanTeach == request.IsTeacher.Value);
        if (request.IsActive.HasValue) employees = employees.Where(x => (x.State == EmployeeState.Active) == request.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            employees = employees.Where(x => x.EmployeeCode.Contains(search) || x.FullName.Contains(search)
                || (x.Phone != null && x.Phone.Contains(search)) || (x.Email != null && x.Email.Contains(search)));
        }
        var total = await employees.CountAsync(ct);
        var rows = await (
            from e in employees
            join d in _designations.GetQueryable().AsNoTracking()
                on new { e.TenantId, Id = e.DesignationId } equals new { d.TenantId, d.Id }
            join o in _organizations.GetQueryable().AsNoTracking()
                on new { e.TenantId, Id = e.OrganizationUnitId } equals new { o.TenantId, Id = (long?)o.Id } into offices
            from o in offices.DefaultIfEmpty()
            orderby e.EmployeeCode, e.Id
            select new { e.PublicId, e.EmployeeCode, e.FullName, e.Phone, e.Email, Designation = d.Name,
                Department = o == null ? null : o.Name, e.JoiningDate, e.CanTeach, e.State, e.RowVersion })
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var items = rows.Select(x => new HrEmployeeRowDto
        {
            Reference = x.PublicId, EmployeeCode = x.EmployeeCode, FullName = x.FullName,
            Phone = x.Phone ?? string.Empty, Email = x.Email, Designation = x.Designation,
            Department = x.Department, JoiningDate = x.JoiningDate.ToDateTime(TimeOnly.MinValue),
            IsTeacher = x.CanTeach, IsActive = x.State == EmployeeState.Active,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();
        return ApiResponse<PagedResult<HrEmployeeRowDto>>.SuccessResponse(new PagedResult<HrEmployeeRowDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = size
        });
    }

    public async Task<ApiResponse<PagedResult<HrLeaveRowDto>>> GetEmployeeLeavesAsync(HrLeaveQueryDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return ApiResponse<PagedResult<HrLeaveRowDto>>.ErrorResponse("HR access is required.", 403);
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var tenantId = _user.TenantId;
        var query = from l in _leaves.GetQueryable().AsNoTracking()
            join e in _employees.GetQueryable().AsNoTracking()
                on new { l.TenantId, Id = l.EmployeeId } equals new { e.TenantId, e.Id }
            join t in _leaveTypes.GetQueryable().AsNoTracking()
                on new { l.TenantId, Id = l.LeaveTypeId } equals new { t.TenantId, t.Id }
            where l.TenantId == tenantId
            select new { l, e, t };
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<LeaveState>(request.Status.Trim(), true, out var state) || !Enum.IsDefined(state))
                return ApiResponse<PagedResult<HrLeaveRowDto>>.ErrorResponse("Invalid leave status.", 400);
            query = query.Where(x => x.l.State == state);
        }
        if (request.From.HasValue)
        {
            var from = DateOnly.FromDateTime(request.From.Value);
            query = query.Where(x => x.l.ToDate >= from);
        }
        if (request.To.HasValue)
        {
            var to = DateOnly.FromDateTime(request.To.Value);
            query = query.Where(x => x.l.FromDate <= to);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(x => x.e.EmployeeCode.Contains(search) || x.e.FullName.Contains(search));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.l.FromDate).ThenByDescending(x => x.l.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new { x.l.Id, EmployeeReference = x.e.PublicId, x.e.EmployeeCode, EmployeeName = x.e.FullName,
                LeaveType = x.t.Name, x.l.FromDate, x.l.ToDate, x.l.TotalDays, x.l.Reason, x.l.State, x.l.ReviewNote })
            .ToListAsync(ct);
        var items = rows.Select(x => new HrLeaveRowDto
        {
            Id = x.Id, EmployeeReference = x.EmployeeReference, EmployeeCode = x.EmployeeCode,
            EmployeeName = x.EmployeeName, LeaveType = x.LeaveType,
            FromDate = x.FromDate.ToDateTime(TimeOnly.MinValue), ToDate = x.ToDate.ToDateTime(TimeOnly.MinValue),
            TotalDays = x.TotalDays, Reason = x.Reason, Status = x.State.ToString(), Remarks = x.ReviewNote
        }).ToList();
        return ApiResponse<PagedResult<HrLeaveRowDto>>.SuccessResponse(new PagedResult<HrLeaveRowDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = size
        });
    }

    public async Task<ApiResponse<bool>> ReviewEmployeeLeaveAsync(ReviewEmployeeLeaveDto request, CancellationToken ct = default)
    {
        if (!CanHr()) return ApiResponse<bool>.ErrorResponse("HR access is required.", 403);
        if (!Enum.TryParse<LeaveState>(request.Status, true, out var decision)
            || decision is not (LeaveState.Approved or LeaveState.Rejected))
            return ApiResponse<bool>.ErrorResponse("Invalid leave decision.", 400);
        var tenantId = _user.TenantId;
        var row = await _leaves.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.Id, ct);
        if (row == null) return ApiResponse<bool>.ErrorResponse("Employee leave application not found.", 404);
        if (row.State != LeaveState.Submitted)
            return ApiResponse<bool>.ErrorResponse("Only submitted leave applications can be reviewed.", 409);
        var exists = await _employees.GetQueryable().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == row.EmployeeId, ct);
        if (!exists) return ApiResponse<bool>.ErrorResponse("Employee leave application not found.", 404);
        var now = _clock.GetUtcNow().UtcDateTime;
        row.State = decision;
        row.ReviewNote = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        row.ReviewedByUserId = _user.UserId;
        row.ReviewedAt = now;
        row.UpdatedBy = _user.UserId;
        row.UpdatedAt = now;
        try
        {
            await _uow.SaveChangesAsync(ct);
            return ApiResponse<bool>.SuccessResponse(true, "Leave application reviewed.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<bool>.ErrorResponse("This leave application was already changed.", 409);
        }
    }

    private bool CanHr() => _user.IsAuthenticated && _user.TenantId > 0
        && (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("HR"));
}
