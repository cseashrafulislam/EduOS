using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IEmployeeAttendanceRepository : IGenericRepository<EmployeeAttendance>
    {
        Task<List<EmployeeAttendance>> GetByDateAsync(DateTime date, long tenantId);
        Task<List<EmployeeAttendance>> GetByEmployeeRangeAsync(long employeeId, DateTime fromDate, DateTime toDate);
        Task<EmployeeAttendance?> GetByEmployeeAndDateAsync(long employeeId, DateTime date);
        Task<int> GetPresentCountAsync(long employeeId, int month, int year);
    }
}
