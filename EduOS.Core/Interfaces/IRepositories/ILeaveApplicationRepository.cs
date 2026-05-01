using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ILeaveApplicationRepository : IGenericRepository<LeaveApplication>
    {
        Task<List<LeaveApplication>> GetByUserAsync(int userId);
        Task<List<LeaveApplication>> GetPendingAsync(int tenantId);
        Task<int> GetUsedDaysAsync(int userId, int leaveTypeId, int year);
    }
}
