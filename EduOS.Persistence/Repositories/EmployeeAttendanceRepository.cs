using EduOS.Core.Entities.Attendance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class EmployeeAttendanceRepository : GenericRepository<EmployeeAttendance>, IEmployeeAttendanceRepository
    {
        public EmployeeAttendanceRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<EmployeeAttendance>> GetByDateAsync(DateTime date, long tenantId)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            return await _dbSet
                .AsNoTracking()
                .Include(a => a.Employee)
                .Where(a => a.Date >= dayStart && a.Date < dayEnd && a.TenantId == tenantId)
                .ToListAsync();
        }

        public async Task<List<EmployeeAttendance>> GetByEmployeeRangeAsync(long employeeId, DateTime fromDate, DateTime toDate)
        {
            var rangeStart = fromDate.Date;
            var rangeEndExclusive = toDate.Date.AddDays(1);

            return await _dbSet
                .AsNoTracking()
                .Where(a => a.EmployeeId == employeeId
                    && a.Date >= rangeStart
                    && a.Date < rangeEndExclusive)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }

        public async Task<EmployeeAttendance?> GetByEmployeeAndDateAsync(long employeeId, DateTime date)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            return await _dbSet
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date >= dayStart && a.Date < dayEnd);
        }

        public async Task<int> GetPresentCountAsync(long employeeId, int month, int year)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEndExclusive = monthStart.AddMonths(1);

            return await _dbSet.CountAsync(a => a.EmployeeId == employeeId
                && a.Date >= monthStart
                && a.Date < monthEndExclusive
                && a.Status == "Present");
        }
    }
}
