using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IEmployeeAttendanceRepository : IGenericRepository<EmployeeAttendance>
    {
        Task<List<EmployeeAttendance>> GetByDateAsync(DateTime date, int tenantId);
        Task<List<EmployeeAttendance>> GetByEmployeeRangeAsync(int employeeId, DateTime fromDate, DateTime toDate);
        Task<EmployeeAttendance?> GetByEmployeeAndDateAsync(int employeeId, DateTime date);
        Task<int> GetPresentCountAsync(int employeeId, int month, int year);
    }
}
