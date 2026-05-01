using EduOS.Core.Entities.Attendance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class EmployeeAttendanceRepository : GenericRepository<EmployeeAttendance>, IEmployeeAttendanceRepository
    {
        public EmployeeAttendanceRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<EmployeeAttendance>> GetByDateAsync(DateTime date, int tenantId)
        {
            return await _dbSet
                .Include(a => a.Employee)
                .Where(a => a.Date.Date == date.Date && a.TenantId == tenantId)
                .ToListAsync();
        }

        public async Task<List<EmployeeAttendance>> GetByEmployeeRangeAsync(int employeeId, DateTime fromDate, DateTime toDate)
        {
            return await _dbSet
                .Where(a => a.EmployeeId == employeeId 
                    && a.Date.Date >= fromDate.Date 
                    && a.Date.Date <= toDate.Date)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }

        public async Task<EmployeeAttendance?> GetByEmployeeAndDateAsync(int employeeId, DateTime date)
        {
            return await _dbSet
                .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date.Date == date.Date);
        }

        public async Task<int> GetPresentCountAsync(int employeeId, int month, int year)
        {
            return await _dbSet.CountAsync(a => a.EmployeeId == employeeId 
                && a.Date.Month == month 
                && a.Date.Year == year 
                && a.Status == "Present");
        }
    }
}
