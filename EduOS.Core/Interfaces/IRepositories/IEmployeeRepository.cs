using EduOS.Core.Entities.HR;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Employee identity belongs to Employee; organization hierarchy belongs to OrganizationUnit; numbers are issued through NumberSeries.</summary>
public interface IEmployeeRepository : IGenericRepository<Employee>
{
    Task<Employee?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Employee?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default);
    Task<(List<Employee> Items, int TotalCount)> GetTeachingStaffAsync(long tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<(List<Employee> Items, int TotalCount)> GetByOrganizationUnitAsync(long organizationUnitId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null, CancellationToken cancellationToken = default);
}
