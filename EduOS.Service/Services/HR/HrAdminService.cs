using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
namespace EduOS.Service.Services.HR;
public sealed class HrAdminService:IHrAdminService
{
 private readonly IGenericRepository<Employee> _employees;private readonly IGenericRepository<LeaveApplication> _leaves;private readonly IUnitOfWork _uow;private readonly ICurrentUserService _user;private readonly TimeProvider _clock;
 public HrAdminService(IGenericRepository<Employee> employees,IGenericRepository<LeaveApplication> leaves,IUnitOfWork uow,ICurrentUserService user,TimeProvider clock){_employees=employees;_leaves=leaves;_uow=uow;_user=user;_clock=clock;}
 public async Task<ApiResponse<PagedResult<HrEmployeeRowDto>>> GetEmployeesAsync(HrEmployeeQueryDto r,CancellationToken ct=default)
 {
  if(!CanHr())return ApiResponse<PagedResult<HrEmployeeRowDto>>.ErrorResponse("HR access is required.",403);var page=Math.Max(1,r.Page);var size=Math.Clamp(r.PageSize,1,100);var q=_employees.GetQueryable().AsNoTracking().Where(x=>x.TenantId==_user.TenantId);
  if(r.DepartmentId.HasValue)q=q.Where(x=>x.DepartmentId==r.DepartmentId);if(r.IsTeacher.HasValue)q=q.Where(x=>x.IsTeacher==r.IsTeacher);if(r.IsActive.HasValue)q=q.Where(x=>x.IsActive==r.IsActive);
  if(!string.IsNullOrWhiteSpace(r.Search)){var s=r.Search.Trim();q=q.Where(x=>x.EmployeeCode.Contains(s)||x.FullName.Contains(s)||x.Phone.Contains(s)||(x.Email!=null&&x.Email.Contains(s)));}
  var total=await q.CountAsync(ct);var items=await q.OrderBy(x=>x.EmployeeCode).Skip((page-1)*size).Take(size).Select(x=>new HrEmployeeRowDto{Reference=x.PublicId,EmployeeCode=x.EmployeeCode,FullName=x.FullName,Phone=x.Phone,Email=x.Email,Designation=x.Designation!=null?x.Designation.Name:string.Empty,Department=x.Department!=null?x.Department.Name:null,JoiningDate=x.JoiningDate,IsTeacher=x.IsTeacher,IsActive=x.IsActive,RowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct);
  return ApiResponse<PagedResult<HrEmployeeRowDto>>.SuccessResponse(new PagedResult<HrEmployeeRowDto>{Items=items,TotalCount=total,Page=page,PageSize=size});
 }
 public async Task<ApiResponse<PagedResult<HrLeaveRowDto>>> GetEmployeeLeavesAsync(HrLeaveQueryDto r,CancellationToken ct=default)
 {
  if(!CanHr())return ApiResponse<PagedResult<HrLeaveRowDto>>.ErrorResponse("HR access is required.",403);var page=Math.Max(1,r.Page);var size=Math.Clamp(r.PageSize,1,100);var t=_user.TenantId;var q=from l in _leaves.GetQueryable().AsNoTracking() join e in _employees.GetQueryable().AsNoTracking() on new{l.TenantId,l.UserId} equals new{e.TenantId,UserId=e.UserId??-1} where l.TenantId==t&&l.UserType=="Employee" select new{l,e};
  if(!string.IsNullOrWhiteSpace(r.Status)){var status=r.Status.Trim();q=q.Where(x=>x.l.Status==status);}if(r.From.HasValue){var from=r.From.Value.Date;q=q.Where(x=>x.l.ToDate>=from);}if(r.To.HasValue){var to=r.To.Value.Date;q=q.Where(x=>x.l.FromDate<=to);}
  if(!string.IsNullOrWhiteSpace(r.Search)){var s=r.Search.Trim();q=q.Where(x=>x.e.EmployeeCode.Contains(s)||x.e.FullName.Contains(s));}
  var total=await q.CountAsync(ct);var items=await q.OrderByDescending(x=>x.l.FromDate).ThenByDescending(x=>x.l.Id).Skip((page-1)*size).Take(size).Select(x=>new HrLeaveRowDto{Id=x.l.Id,EmployeeReference=x.e.PublicId,EmployeeCode=x.e.EmployeeCode,EmployeeName=x.e.FullName,LeaveType=x.l.LeaveType!=null?x.l.LeaveType.Name:string.Empty,FromDate=x.l.FromDate,ToDate=x.l.ToDate,TotalDays=x.l.TotalDays,Reason=x.l.Reason,Status=x.l.Status,Remarks=x.l.Remarks}).ToListAsync(ct);
  return ApiResponse<PagedResult<HrLeaveRowDto>>.SuccessResponse(new PagedResult<HrLeaveRowDto>{Items=items,TotalCount=total,Page=page,PageSize=size});
 }
 public async Task<ApiResponse<bool>> ReviewEmployeeLeaveAsync(ReviewEmployeeLeaveDto r,CancellationToken ct=default)
 {
  if(!CanHr())return ApiResponse<bool>.ErrorResponse("HR access is required.",403);var t=_user.TenantId;var row=await _leaves.GetQueryable().FirstOrDefaultAsync(x=>x.TenantId==t&&x.Id==r.Id&&x.UserType=="Employee",ct);if(row==null)return ApiResponse<bool>.ErrorResponse("Employee leave application not found.",404);if(row.Status!="Pending")return ApiResponse<bool>.ErrorResponse("Only pending leave applications can be reviewed.",409);
  var ownsEmployee=await _employees.GetQueryable().AsNoTracking().AnyAsync(x=>x.TenantId==t&&x.UserId==row.UserId,ct);if(!ownsEmployee)return ApiResponse<bool>.ErrorResponse("Employee leave application not found.",404);var now=_clock.GetUtcNow().UtcDateTime;row.Status=r.Status;row.Remarks=string.IsNullOrWhiteSpace(r.Remarks)?null:r.Remarks.Trim();row.ApprovedBy=_user.UserId;row.ApprovedAt=now;row.UpdatedBy=_user.UserId;row.UpdatedAt=now;await _uow.SaveChangesAsync(ct);return ApiResponse<bool>.SuccessResponse(true,"Leave application reviewed.");
 }
 private bool CanHr()=>_user.IsAuthenticated&&_user.TenantId>0&&(_user.IsTenantAdmin||_user.IsInRole("Principal")||_user.IsInRole("HR"));
}
