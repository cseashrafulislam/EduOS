using EduOS.Core.Entities.Attendance;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Leave applications are EmployeeId-owned; approved leave usage is decimal and calculated from approved requests.</summary>
public interface IEmployeeLeaveApplicationRepository : IGenericRepository<EmployeeLeaveApplication>
{
    Task<(List<EmployeeLeaveApplication> Items, int TotalCount)> GetByEmployeeAsync(long employeeId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<EmployeeLeaveApplication> Items, int TotalCount)> GetByStateAsync(LeaveState state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<decimal> GetApprovedUsedDaysAsync(long employeeId, long leaveTypeId, int year, CancellationToken cancellationToken = default);
}
