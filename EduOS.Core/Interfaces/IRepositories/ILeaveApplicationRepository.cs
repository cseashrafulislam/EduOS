using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories;

public interface ILeaveApplicationRepository : IGenericRepository<EmployeeLeaveApplication>
{
    Task<List<EmployeeLeaveApplication>> GetByUserAsync(long userId);
    Task<List<EmployeeLeaveApplication>> GetPendingAsync(long tenantId);
    Task<int> GetUsedDaysAsync(long userId, long leaveTypeId, int year);
}
