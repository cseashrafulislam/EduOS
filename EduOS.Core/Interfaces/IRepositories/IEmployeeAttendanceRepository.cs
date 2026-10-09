using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Employee attendance uses a DateOnly business date and EmployeeId, not application user identity.</summary>
public interface IEmployeeAttendanceRepository : IGenericRepository<EmployeeAttendance>
{
    Task<(List<EmployeeAttendance> Items, int TotalCount)> GetByDateAsync(DateOnly attendanceDate, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<EmployeeAttendance> Items, int TotalCount)> GetByEmployeeRangeAsync(long employeeId, DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<EmployeeAttendance?> GetByEmployeeAndDateAsync(long employeeId, DateOnly attendanceDate, CancellationToken cancellationToken = default);
    Task<int> GetPresentCountAsync(long employeeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
