using EduOS.Core.Entities.Attendance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class LeaveApplicationRepository : GenericRepository<LeaveApplication>, ILeaveApplicationRepository
    {
        public LeaveApplicationRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<LeaveApplication>> GetByUserAsync(int userId)
        {
            return await _dbSet
                .Include(l => l.LeaveType)
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<LeaveApplication>> GetPendingAsync(int tenantId)
        {
            return await _dbSet
                .Include(l => l.LeaveType)
                .Where(l => l.TenantId == tenantId && l.Status == "Pending")
                .OrderBy(l => l.CreatedAt)
                .ToListAsync();
        }

        public async Task<int> GetUsedDaysAsync(int userId, int leaveTypeId, int year)
        {
            return await _dbSet
                .Where(l => l.UserId == userId 
                    && l.LeaveTypeId == leaveTypeId 
                    && l.FromDate.Year == year 
                    && l.Status == "Approved")
                .SumAsync(l => l.TotalDays);
        }
    }
}
