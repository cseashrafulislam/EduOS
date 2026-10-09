using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class EmployeeRepository : GenericRepository<Employee>, IEmployeeRepository
{
    public EmployeeRepository(EduOSDbContext context) : base(context) { }

    public Task<Employee?> GetByCodeAsync(string code) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeCode == code);

    public Task<Employee?> GetByUserIdAsync(long userId) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);

    public Task<List<Employee>> GetTeachersAsync(long tenantId) =>
        _dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.CanTeach && x.State == EmployeeState.Active)
            .OrderBy(x => x.FullName).ToListAsync();

    public Task<List<Employee>> GetByDepartmentAsync(long departmentId) =>
        _dbSet.AsNoTracking().Where(x => x.OrganizationUnitId == departmentId && x.State == EmployeeState.Active)
            .OrderBy(x => x.FullName).ToListAsync();

    public async Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null)
    {
        var normalized = code.Trim();
        var query = _dbSet.Where(x => x.TenantId == tenantId && x.EmployeeCode == normalized);
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    public Task<string> GenerateEmployeeCodeAsync(long tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        return Task.FromResult($"EMP-{tenantId}-{suffix}");
    }

    public Task<Employee?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeCode == code, cancellationToken);
    public Task<Employee?> GetByUserIdAsync(long userId, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    public Task<(List<Employee> Items, int TotalCount)> GetTeachingStaffAsync(long tenantId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.TenantId == tenantId && x.CanTeach && x.State == EmployeeState.Active)
            .OrderBy(x => x.FullName).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<Employee> Items, int TotalCount)> GetByOrganizationUnitAsync(long organizationUnitId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.OrganizationUnitId == organizationUnitId)
            .OrderBy(x => x.FullName).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    public Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim();
        return _dbSet.AnyAsync(x => x.TenantId == tenantId && x.EmployeeCode == normalized
            && (!excludeId.HasValue || x.Id != excludeId.Value), cancellationToken);
    }
}
