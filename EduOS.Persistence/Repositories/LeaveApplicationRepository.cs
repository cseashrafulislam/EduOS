using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class LeaveApplicationRepository : GenericRepository<EmployeeLeaveApplication>, IEmployeeLeaveApplicationRepository
{
    public LeaveApplicationRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<EmployeeLeaveApplication>> GetByUserAsync(long userId)
    {
        var employeeIds = _context.Set<Employee>().Where(x => x.UserId == userId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId))
            .OrderByDescending(x => x.CreatedAt).ToListAsync();
    }

    public Task<List<EmployeeLeaveApplication>> GetPendingAsync(long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.State == LeaveState.Submitted)
            .OrderBy(x => x.CreatedAt).ToListAsync();

    public async Task<int> GetUsedDaysAsync(long userId, long leaveTypeId, int year)
    {
        var employeeIds = _context.Set<Employee>().Where(x => x.UserId == userId).Select(x => x.Id);
        var days = await _dbSet.Where(x => employeeIds.Contains(x.EmployeeId) && x.LeaveTypeId == leaveTypeId
            && x.FromDate.Year == year && x.State == LeaveState.Approved).SumAsync(x => x.TotalDays);
        return (int)Math.Ceiling(days);
    }

    public Task<(List<EmployeeLeaveApplication> Items, int TotalCount)> GetByEmployeeAsync(long employeeId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.FromDate).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<EmployeeLeaveApplication> Items, int TotalCount)> GetByStateAsync(LeaveState state, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.State == state)
            .OrderByDescending(x => x.FromDate).ThenByDescending(x => x.Id), page, pageSize, cancellationToken);
    public Task<decimal> GetApprovedUsedDaysAsync(long employeeId, long leaveTypeId, int year, CancellationToken cancellationToken)
    {
        var from = new DateOnly(year, 1, 1);
        var until = from.AddYears(1);
        return _dbSet.Where(x => x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId
            && x.FromDate >= from && x.FromDate < until && x.State == LeaveState.Approved)
            .SumAsync(x => x.TotalDays, cancellationToken);
    }
}
