using EduOS.Core.Entities.Attendance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class EmployeeAttendanceRepository : GenericRepository<EmployeeAttendance>, IEmployeeAttendanceRepository
{
    public EmployeeAttendanceRepository(EduOSDbContext context) : base(context) { }

    public Task<List<EmployeeAttendance>> GetByDateAsync(DateTime date, long tenantId)
    {
        var target = DateOnly.FromDateTime(date);
        return _dbSet.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AttendanceDate == target)
            .OrderBy(x => x.EmployeeId)
            .ToListAsync();
    }

    public Task<List<EmployeeAttendance>> GetByEmployeeRangeAsync(long employeeId, DateTime fromDate, DateTime toDate)
    {
        var from = DateOnly.FromDateTime(fromDate);
        var to = DateOnly.FromDateTime(toDate);
        return _dbSet.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.AttendanceDate >= from && x.AttendanceDate <= to)
            .OrderBy(x => x.AttendanceDate)
            .ToListAsync();
    }

    public Task<EmployeeAttendance?> GetByEmployeeAndDateAsync(long employeeId, DateTime date)
    {
        var target = DateOnly.FromDateTime(date);
        return _dbSet.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.AttendanceDate == target);
    }

    public Task<int> GetPresentCountAsync(long employeeId, int month, int year)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        return _dbSet.CountAsync(x => x.EmployeeId == employeeId
            && x.AttendanceDate >= start && x.AttendanceDate < end
            && x.State == AttendanceState.Present);
    }

    public Task<(List<EmployeeAttendance> Items, int TotalCount)> GetByDateAsync(DateOnly attendanceDate, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.AttendanceDate == attendanceDate)
            .OrderBy(x => x.EmployeeId).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<EmployeeAttendance> Items, int TotalCount)> GetByEmployeeRangeAsync(long employeeId, DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.AttendanceDate >= fromDate && x.AttendanceDate <= toDate)
            .OrderBy(x => x.AttendanceDate).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    public Task<EmployeeAttendance?> GetByEmployeeAndDateAsync(long employeeId, DateOnly attendanceDate, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.AttendanceDate == attendanceDate, cancellationToken);
    public Task<int> GetPresentCountAsync(long employeeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        _dbSet.CountAsync(x => x.EmployeeId == employeeId && x.AttendanceDate >= fromDate && x.AttendanceDate <= toDate
            && x.State == AttendanceState.Present, cancellationToken);
}
