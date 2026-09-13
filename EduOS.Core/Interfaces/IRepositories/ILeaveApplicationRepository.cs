using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ILeaveApplicationRepository : IGenericRepository<LeaveApplication>
    {
        Task<List<LeaveApplication>> GetByUserAsync(long userId);
        Task<List<LeaveApplication>> GetPendingAsync(long tenantId);
        Task<int> GetUsedDaysAsync(long userId, long leaveTypeId, int year);
    }
}
